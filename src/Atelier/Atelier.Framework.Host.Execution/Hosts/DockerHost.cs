using System.Text.RegularExpressions;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Atelier.Framework.Host.Execution.Hosts;

public partial class DockerHost : IHost, IAsyncDisposable, IAtelier
{
    [Requisite] private readonly IDockerClient _dockerClient = null!;

    private HostState _state = HostState.Pending;
    private bool _disposed;

    public string HostId { get; } = Guid.NewGuid().ToString();
    public OfferingExecutionMode ExecutionMode => OfferingExecutionMode.NetworkMapped;
    public HostState State => _state;

    public string? NetworkAddress { get; private set; }
    public int? NetworkPort { get; private set; }
    public int? ProcessId => null;

    public string? ContainerId { get; private set; }
    public string? ContainerName { get; private set; }
    public string? ImageName { get; private set; }

    public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>();

    private const int OPERATION_TIMEOUT_SECONDS = 120;
    private const int MAX_TRANSIENT_RETRIES = 4;
    private const int INITIAL_BACKOFF_MS = 250;
    private const int MAX_BACKOFF_MS = 4000;

    public async Task CreateContainerAsync(ExecutionOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DockerImage);

        ImageName = options.DockerImage;
        ContainerName = options.DockerContainerName ?? $"atelier-{Guid.NewGuid():N}";

        var imageGate = ValidateImageReference(
            options.DockerImage,
            options.AllowedImageRegistries);
        if (!imageGate.IsSuccess)
        {
            _state = HostState.Failed;

            throw new InvalidOperationException(
                $"Docker image '{options.DockerImage}' was rejected before container creation.");
        }

        try
        {
            Observe(LogLevel.Debug);

            await EnsureImageExistsAsync(options.DockerImage, cancellationToken).ConfigureAwait(false);

            var createParams = new CreateContainerParameters
            {
                Image = options.DockerImage,
                Name = ContainerName,
                Env = options.EnvironmentVariables
                    .Select(kvp => $"{kvp.Key}={kvp.Value}")
                    .ToList(),
                Labels = options.DockerLabels,
                HostConfig = new HostConfig
                {
                    PublishAllPorts = true,
                    AutoRemove = false,
                    RestartPolicy = new RestartPolicy
                    {
                        Name = RestartPolicyKind.UnlessStopped
                    }
                }
            };

            if (options.ResourceLimits != null)
            {
                if (options.ResourceLimits.MaxMemoryBytes.HasValue)
                {
                    createParams.HostConfig.Memory = options.ResourceLimits.MaxMemoryBytes.Value;
                }
                createParams.HostConfig.CPUPercent = options.ResourceLimits.MaxCpuPercent ?? 0;
            }

            if (options.ExposedPorts.Count > 0)
            {
                createParams.ExposedPorts = options.ExposedPorts
                    .ToDictionary(
                        port => $"{port}/tcp",
                        _ => new EmptyStruct());
            }

            var response = await ExecuteWithRetryAsync(
                token => _dockerClient.Containers.CreateContainerAsync(createParams, token),
                "CreateContainer",
                cancellationToken).ConfigureAwait(false);
            ContainerId = response.ID;

            Observe(LogLevel.Information, values: SecretRedaction.Redact(
                options.SecretClaims,
                ("ContainerId", ContainerId),
                ("ContainerName", ContainerName),
                ("ImageName", options.DockerImage)));
        }
        catch (Exception ex)
        {
            _state = HostState.Failed;

            Observe(LogLevel.Error, ex);

            throw;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrEmpty(ContainerId))
        {
            throw new InvalidOperationException("Container not created. Call CreateContainerAsync first.");
        }

        var containerId = ContainerId;

        _state = HostState.Starting;

        try
        {
            Observe(LogLevel.Debug);

            var started = await ExecuteWithRetryAsync(
                token => _dockerClient.Containers.StartContainerAsync(
                    containerId,
                    new ContainerStartParameters(),
                    token),
                "StartContainer",
                cancellationToken).ConfigureAwait(false);

            if (!started)
            {
                throw new InvalidOperationException($"Failed to start container {containerId}");
            }

            var inspectResponse = await WaitForContainerReadyAsync(
                containerId,
                TimeSpan.FromSeconds(30),
                cancellationToken).ConfigureAwait(false);

            if (inspectResponse.NetworkSettings?.Ports != null)
            {
                var firstPort = inspectResponse.NetworkSettings.Ports.FirstOrDefault();
                if (firstPort.Value != null && firstPort.Value.Count > 0)
                {
                    NetworkAddress = firstPort.Value[0].HostIP;
                    if (int.TryParse(firstPort.Value[0].HostPort, out var port))
                    {
                        NetworkPort = port;
                    }
                }
            }

            _state = HostState.Running;

            Observe(LogLevel.Information, values: [("ContainerId", ContainerId), ("ContainerName", ContainerName ?? ""), ("NetworkAddress", NetworkAddress ?? "none"), ("NetworkPort", NetworkPort ?? 0)]);
        }
        catch (Exception ex)
        {
            _state = HostState.Failed;

            Observe(LogLevel.Error, ex);

            await ForceRemoveContainerAsync(containerId).ConfigureAwait(false);

            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ContainerId))
        {
            return;
        }

        var containerId = ContainerId;

        _state = HostState.Stopping;

        try
        {
            Observe(LogLevel.Debug);

            await ExecuteWithRetryAsync(
                async token =>
                {
                    await _dockerClient.Containers.StopContainerAsync(
                        containerId,
                        new ContainerStopParameters { WaitBeforeKillSeconds = 10 },
                        token).ConfigureAwait(false);
                    return true;
                },
                "StopContainer",
                cancellationToken).ConfigureAwait(false);

            await _dockerClient.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                cancellationToken).ConfigureAwait(false);

            _state = HostState.Stopped;

            Observe(LogLevel.Information, values: [("ContainerId", ContainerId), ("ContainerName", ContainerName ?? "")]);
        }
        catch (Exception ex)
        {
            _state = HostState.Failed;

            Observe(LogLevel.Error, ex);

            throw;
        }
    }

    private const string IMAGE_REFERENCE_PATTERN =
        @"^(?<registry>(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?)+(?::[0-9]+)?|[a-zA-Z0-9]+:[0-9]+|localhost(?::[0-9]+)?)/)?(?<repository>[a-z0-9]+(?:(?:[._]|__|[-]+)[a-z0-9]+)*(?:/[a-z0-9]+(?:(?:[._]|__|[-]+)[a-z0-9]+)*)*)(?::(?<tag>[a-zA-Z0-9_][a-zA-Z0-9._-]{0,127}))?(?:@(?<digest>sha256:[a-f0-9]{64}))?$";

    private static readonly Regex ImageReferenceRegex = new(
        IMAGE_REFERENCE_PATTERN,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private Outcome ValidateImageReference(
        string imageReference,
        IReadOnlyCollection<string> allowedRegistries)
    {
        if (string.IsNullOrWhiteSpace(imageReference))
        {
            Observe(
                LogLevel.Error,
                null,
                values: [("Reason", "Docker image reference is empty")]);
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "DockerImage", imageReference);

        var match = ImageReferenceRegex.Match(imageReference);
        if (!match.Success)
        {
            Observe(
                LogLevel.Error,
                null,
                values: [("Reason", "Docker image reference is not a well-formed [registry/]repository[:tag|@sha256:digest] value"), ("ImageName", imageReference)]);
            return Outcome.Failure();
        }

        if (allowedRegistries.Count == 0)
        {
            Observe(
                LogLevel.Error,
                null,
                values: [("Reason", "No Docker image registry allowlist is configured; refusing to run an unvetted image. Populate ExecutionOptions.AllowedImageRegistries"), ("ImageName", imageReference)]);
            return Outcome.Failure();
        }

        var registry = match.Groups["registry"].Value.TrimEnd('/');
        var repository = match.Groups["repository"].Value;
        var canonical = string.IsNullOrEmpty(registry)
            ? repository
            : $"{registry}/{repository}";

        foreach (var allowed in allowedRegistries)
        {
            if (string.IsNullOrWhiteSpace(allowed))
            {
                continue;
            }

            var prefix = allowed.Trim().TrimEnd('/');
            if (string.Equals(registry, prefix, StringComparison.Ordinal)
                || string.Equals(canonical, prefix, StringComparison.Ordinal)
                || canonical.StartsWith($"{prefix}/", StringComparison.Ordinal))
            {
                return Outcome.Success();
            }
        }

        Observe(
            LogLevel.Error,
            null,
            values: [("Reason", "Docker image is not permitted by the configured registry allowlist"), ("ImageName", imageReference)]);
        return Outcome.Failure();
    }

    private async Task EnsureImageExistsAsync(string imageName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);

        try
        {
            var images = await ExecuteWithRetryAsync(
                token => _dockerClient.Images.ListImagesAsync(
                    new ImagesListParameters
                    {
                        Filters = new Dictionary<string, IDictionary<string, bool>>
                        {
                            ["reference"] = new Dictionary<string, bool> { [imageName] = true }
                        }
                    },
                    token),
                "ListImages",
                cancellationToken).ConfigureAwait(false);

            if (images.Count == 0)
            {
                Observe(LogLevel.Information);

                await ExecuteWithRetryAsync(
                    async token =>
                    {
                        await _dockerClient.Images.CreateImageAsync(
                            new ImagesCreateParameters { FromImage = imageName },
                            null,
                            new Progress<JSONMessage>(),
                            token).ConfigureAwait(false);
                        return true;
                    },
                    "CreateImage",
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex);

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_state == HostState.Running)
        {
            await StopAsync().ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrEmpty(ContainerId)
            && _state != HostState.Stopped)
        {
            await ForceRemoveContainerAsync(ContainerId).ConfigureAwait(false);
        }
    }

    private async Task<ContainerInspectResponse> WaitForContainerReadyAsync(
        string containerId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inspectResponse = await _dockerClient.Containers.InspectContainerAsync(
                containerId,
                cancellationToken).ConfigureAwait(false);

            if (inspectResponse.State?.Running == true)
            {
                return inspectResponse;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"Container {containerId} did not become ready within {timeout.TotalSeconds} seconds");
    }

    private async Task ForceRemoveContainerAsync(string containerId)
    {
        try
        {
            await _dockerClient.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Warning, ex);
        }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        var backoffMs = INITIAL_BACKOFF_MS;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(OPERATION_TIMEOUT_SECONDS));

            try
            {
                return await operation(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
                when (attempt < MAX_TRANSIENT_RETRIES
                    && !cancellationToken.IsCancellationRequested
                    && IsTransient(ex))
            {
                attempt++;

                Observe(LogLevel.Warning, ex, values: [("Operation", operationName), ("Attempt", attempt), ("BackoffMs", backoffMs)]);

                await Task.Delay(backoffMs, cancellationToken).ConfigureAwait(false);
                backoffMs = Math.Min(backoffMs * 2, MAX_BACKOFF_MS);
            }
        }
    }

    private static bool IsTransient(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return false;
        }

        if (exception is DockerApiException apiException)
        {
            var statusCode = (int)apiException.StatusCode;
            return statusCode == 429
                || statusCode >= 500;
        }

        return exception is HttpRequestException
            || exception is System.IO.IOException
            || exception is TimeoutException;
    }
}
