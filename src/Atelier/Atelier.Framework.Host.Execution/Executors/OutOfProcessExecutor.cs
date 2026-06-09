using Atelier.Framework.Primitives;
using System.Diagnostics;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Host.Execution.Hosts;

namespace Atelier.Framework.Host.Execution;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class OutOfProcessExecutor : IExecutor, IAtelier
{
    public OfferingExecutionMode ExecutionMode => OfferingExecutionMode.OutOfProcess;

    public async Task<HostExecutionContext> StartOfferingAsync(
        Type offeringType,
        ExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offeringType);
        ArgumentNullException.ThrowIfNull(options);

        if (options.ProcessLaunch is null
            || string.IsNullOrWhiteSpace(options.ProcessLaunch.Executable))
        {
            throw new ArgumentException(
                $"{nameof(ExecutionOptions.ProcessLaunch)} with an executable is required.",
                nameof(options));
        }

        var process = BuildProcess(options);
        var host = new OutOfProcessHost(Logger).Configure(
            process,
            options.NetworkAddress,
            options.NetworkPort);

        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        Observe(LogLevel.Information, values: SecretRedaction.Redact(
            options.SecretClaims,
            ("OfferingType", offeringType.FullName ?? offeringType.Name),
            ("Executable", options.ProcessLaunch.Executable),
            ("ProcessId", host.ProcessId ?? 0)));

        return new HostExecutionContext
        {
            OfferingType = offeringType,
            OfferingTypeName = offeringType.FullName ?? offeringType.Name,
            ExecutionMode = OfferingExecutionMode.OutOfProcess,
            State = host.State,
            Host = host,
            NetworkAddress = host.NetworkAddress,
            NetworkPort = host.NetworkPort,
            ProcessId = host.ProcessId,
            StartedAt = DateTime.UtcNow,
            ResourceAllocation = options.ResourceLimits ?? new ResourceAllocation()
        };
    }

    public async Task StopOfferingAsync(
        HostExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Host is not null)
        {
            await context.Host.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        context.State = HostState.Stopped;
        context.StoppedAt = DateTime.UtcNow;
    }

    private static readonly string[] InheritedEnvironmentAllowlist =
    [
        "PATH",
        "HOME",
        "TMPDIR",
        "LANG",
        "LC_ALL",
        "DOTNET_ROOT",
        "DOTNET_CLI_TELEMETRY_OPTOUT",
        "DOTNET_NOLOGO"
    ];

    private static Process BuildProcess(ExecutionOptions options)
    {
        var spec = options.ProcessLaunch!;
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.Executable,
            WorkingDirectory = spec.WorkingDirectory ?? string.Empty,
            UseShellExecute = false
        };

        foreach (var argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();

        foreach (var name in InheritedEnvironmentAllowlist)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null)
            {
                startInfo.Environment[name] = value;
            }
        }

        foreach (var claim in options.SecretClaims)
        {
            if (string.IsNullOrWhiteSpace(claim))
            {
                continue;
            }

            var value = Environment.GetEnvironmentVariable(claim);
            if (value is not null)
            {
                startInfo.Environment[claim] = value;
            }
        }

        foreach (var variable in options.EnvironmentVariables)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        return new Process
        {
            StartInfo = startInfo
        };
    }
}
