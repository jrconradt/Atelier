using System.Reflection;
using Docker.DotNet;
using Atelier.Framework.Host.Execution.Hosts;
using Atelier.Framework.Observability;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Host.Execution;

public static class HostExecutionBehaviorTests
{
    private sealed class StubDockerClientProvider : IDockerClientProvider
    {
        private readonly Lazy<IDockerClient> _client =
            new(() => new DockerClientConfiguration().CreateClient());

        public IDockerClient Client => _client.Value;
    }

    private static void WireField(
        object target,
        string fieldName,
        object value)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field is null)
        {
            throw new InvalidOperationException($"field '{fieldName}' was not found on {target.GetType().Name}");
        }
        field.SetValue(target, value);
    }

    private static ExecutorFactory BuildFactory()
    {
        var factory = new ExecutorFactory();
        WireField(factory, "_inProcessExecutor", new InProcessExecutor((ILogger?)null));
        WireField(factory, "_outOfProcessExecutor", new OutOfProcessExecutor((ILogger?)null));
        WireField(factory, "_dockerExecutor", new DockerExecutor(new StubDockerClientProvider(), (ILogger?)null));
        return factory;
    }

    [GeneratedTest("Host.Execution/SecretRedaction-Masks-Declared-Claim", "global::Atelier.Framework.Host.Execution.SecretRedaction")]
    public static void RedactMasksDeclaredSecretClaimAndKeepsOthers()
    {
        var redacted = SecretRedaction.Redact(
            new[] { "ApiToken" },
            ("OfferingType", "Checkout"),
            ("ApiToken", "super-secret-value"));

        if (redacted.Length != 2)
        {
            throw new InvalidOperationException($"expected two pairs back, got {redacted.Length}");
        }
        if (redacted[0].Key != "OfferingType"
            || (string)redacted[0].Value != "Checkout")
        {
            throw new InvalidOperationException($"non-secret pair was altered: {redacted[0].Key}={redacted[0].Value}");
        }
        if (redacted[1].Key != "ApiToken"
            || (string)redacted[1].Value != SecretRedaction.RedactedPlaceholder)
        {
            throw new InvalidOperationException($"declared secret was not redacted: {redacted[1].Key}={redacted[1].Value}");
        }
    }

    [GeneratedTest("Host.Execution/SecretRedaction-Claim-Match-Is-Case-Insensitive", "global::Atelier.Framework.Host.Execution.SecretRedaction")]
    public static void RedactMatchesDeclaredClaimRegardlessOfCase()
    {
        var redacted = SecretRedaction.Redact(
            new[] { "apitoken" },
            ("ApiToken", "value"));

        if ((string)redacted[0].Value != SecretRedaction.RedactedPlaceholder)
        {
            throw new InvalidOperationException($"case-insensitive claim match failed: {redacted[0].Value}");
        }
    }

    [GeneratedTest("Host.Execution/SecretRedaction-Masks-Heuristic-Sensitive-Key", "global::Atelier.Framework.Host.Execution.SecretRedaction")]
    public static void RedactMasksHeuristicallySensitiveKeyWithoutDeclaration()
    {
        var sensitiveKey = "Password";
        if (!SecretRedaction.IsSensitiveKey(sensitiveKey))
        {
            throw new InvalidOperationException($"expected '{sensitiveKey}' to be treated as sensitive");
        }

        var redacted = SecretRedaction.Redact(
            Array.Empty<string>(),
            (sensitiveKey, "hunter2"));

        if ((string)redacted[0].Value != SecretRedaction.RedactedPlaceholder)
        {
            throw new InvalidOperationException($"heuristic sensitive key was not redacted: {redacted[0].Value}");
        }
    }

    [GeneratedTest("Host.Execution/ResourceAllocation-Megabytes-Roundtrip-Bytes", "global::Atelier.Framework.Host.Execution.ResourceAllocation")]
    public static void MaxMemoryMbConvertsToAndFromBytes()
    {
        var allocation = new ResourceAllocation
        {
            MaxMemoryMB = 512
        };

        if (allocation.MaxMemoryBytes != 512L * 1024 * 1024)
        {
            throw new InvalidOperationException($"expected 536870912 bytes, got {allocation.MaxMemoryBytes}");
        }
        if (allocation.MaxMemoryMB != 512)
        {
            throw new InvalidOperationException($"expected 512 MB back, got {allocation.MaxMemoryMB}");
        }
    }

    [GeneratedTest("Host.Execution/ResourceAllocation-Null-Megabytes-Stays-Null", "global::Atelier.Framework.Host.Execution.ResourceAllocation")]
    public static void MaxMemoryMbNullLeavesBytesNull()
    {
        var allocation = new ResourceAllocation
        {
            MaxMemoryMB = null
        };

        if (allocation.MaxMemoryBytes is not null)
        {
            throw new InvalidOperationException($"expected null bytes, got {allocation.MaxMemoryBytes}");
        }
        if (allocation.MaxMemoryMB is not null)
        {
            throw new InvalidOperationException($"expected null MB, got {allocation.MaxMemoryMB}");
        }
    }

    [GeneratedTest("Host.Execution/ExecutorFactory-Maps-Each-Mode-To-Matching-Executor", "global::Atelier.Framework.Host.Execution.ExecutorFactory")]
    public static void GetExecutorReturnsExecutorWhoseModeMatchesRequest()
    {
        var factory = BuildFactory();

        var inProcess = factory.GetExecutor(OfferingExecutionMode.InProcess);
        if (inProcess.ExecutionMode != OfferingExecutionMode.InProcess)
        {
            throw new InvalidOperationException($"InProcess request resolved to {inProcess.ExecutionMode}");
        }

        var outOfProcess = factory.GetExecutor(OfferingExecutionMode.OutOfProcess);
        if (outOfProcess.ExecutionMode != OfferingExecutionMode.OutOfProcess)
        {
            throw new InvalidOperationException($"OutOfProcess request resolved to {outOfProcess.ExecutionMode}");
        }

        var networkMapped = factory.GetExecutor(OfferingExecutionMode.NetworkMapped);
        if (networkMapped.ExecutionMode != OfferingExecutionMode.NetworkMapped)
        {
            throw new InvalidOperationException($"NetworkMapped request resolved to {networkMapped.ExecutionMode}");
        }
    }

    [GeneratedTest("Host.Execution/ExecutorFactory-Unknown-Mode-Is-Rejected", "global::Atelier.Framework.Host.Execution.ExecutorFactory")]
    public static void GetExecutorRejectsUnmappedMode()
    {
        var factory = BuildFactory();
        var unknownMode = (OfferingExecutionMode)999;

        try
        {
            factory.GetExecutor(unknownMode);
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        throw new InvalidOperationException("expected an unmapped mode to be rejected");
    }

    [GeneratedTest("Host.Execution/InProcessExecutor-Start-Produces-Running-Context", "global::Atelier.Framework.Host.Execution.InProcessExecutor")]
    public static async Task InProcessExecutorStartReturnsRunningContextForOffering()
    {
        var executor = new InProcessExecutor((ILogger?)null);
        var options = new ExecutionOptions
        {
            ExecutionMode = OfferingExecutionMode.InProcess
        };

        var context = await executor.StartOfferingAsync(
            typeof(HostExecutionBehaviorTests),
            options,
            CancellationToken.None).ConfigureAwait(false);

        if (context.ExecutionMode != OfferingExecutionMode.InProcess)
        {
            throw new InvalidOperationException($"expected InProcess context, got {context.ExecutionMode}");
        }
        if (context.State != HostState.Running)
        {
            throw new InvalidOperationException($"expected Running state after start, got {context.State}");
        }
        if (context.OfferingType != typeof(HostExecutionBehaviorTests))
        {
            throw new InvalidOperationException($"context carried the wrong offering type: {context.OfferingType}");
        }
        if (context.StartedAt is null)
        {
            throw new InvalidOperationException("StartedAt was not stamped");
        }

        await executor.StopOfferingAsync(context, CancellationToken.None).ConfigureAwait(false);

        if (context.State != HostState.Stopped)
        {
            throw new InvalidOperationException($"expected Stopped state after stop, got {context.State}");
        }
        if (context.StoppedAt is null)
        {
            throw new InvalidOperationException("StoppedAt was not stamped");
        }
    }

    [GeneratedTest("Host.Execution/OutOfProcessExecutor-Missing-Launch-Spec-Is-Rejected", "global::Atelier.Framework.Host.Execution.OutOfProcessExecutor")]
    public static async Task OutOfProcessExecutorRejectsOptionsWithoutExecutable()
    {
        var executor = new OutOfProcessExecutor((ILogger?)null);
        var options = new ExecutionOptions
        {
            ExecutionMode = OfferingExecutionMode.OutOfProcess
        };

        try
        {
            await executor.StartOfferingAsync(
                typeof(HostExecutionBehaviorTests),
                options,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("expected a missing process-launch spec to be rejected");
    }

    private static ProcessLaunchSpec LongLivedSleepSpec()
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessLaunchSpec
            {
                Executable = "cmd.exe",
                Arguments = { "/c", "ping -n 60 127.0.0.1 > NUL" }
            };
        }

        return new ProcessLaunchSpec
        {
            Executable = "/bin/sh",
            Arguments = { "-c", "sleep 60" }
        };
    }

    [GeneratedTest("Host.Execution/OutOfProcessExecutor-Spawns-Real-Process-And-Stops-It", "global::Atelier.Framework.Host.Execution.OutOfProcessExecutor")]
    public static async Task OutOfProcessExecutorStartsRealProcessThenStopsItCleanly()
    {
        var executor = new OutOfProcessExecutor((ILogger?)null);
        var options = new ExecutionOptions
        {
            ExecutionMode = OfferingExecutionMode.OutOfProcess,
            ProcessLaunch = LongLivedSleepSpec()
        };

        var context = await executor.StartOfferingAsync(
            typeof(HostExecutionBehaviorTests),
            options,
            CancellationToken.None).ConfigureAwait(false);

        if (context.ExecutionMode != OfferingExecutionMode.OutOfProcess)
        {
            throw new InvalidOperationException($"expected OutOfProcess context, got {context.ExecutionMode}");
        }
        if (context.State != HostState.Running)
        {
            throw new InvalidOperationException($"expected Running state after start, got {context.State}");
        }
        if (context.ProcessId is null)
        {
            throw new InvalidOperationException("expected a real process id after start");
        }

        var spawnedPid = context.ProcessId.Value;
        using (var spawned = System.Diagnostics.Process.GetProcessById(spawnedPid))
        {
            if (spawned.HasExited)
            {
                throw new InvalidOperationException($"spawned process {spawnedPid} was not alive after start");
            }
        }

        await executor.StopOfferingAsync(context, CancellationToken.None).ConfigureAwait(false);

        if (context.State != HostState.Stopped)
        {
            throw new InvalidOperationException($"expected Stopped state after stop, got {context.State}");
        }

        try
        {
            using var afterStop = System.Diagnostics.Process.GetProcessById(spawnedPid);
            if (!afterStop.HasExited)
            {
                throw new InvalidOperationException($"spawned process {spawnedPid} survived Stop");
            }
        }
        catch (ArgumentException)
        {
        }
    }

    [GeneratedTest("Host.Execution/InProcessHost-Lifecycle-Transitions-Pending-To-Stopped", "global::Atelier.Framework.Host.Execution.Hosts.InProcessHost")]
    public static async Task InProcessHostMovesThroughRunningThenStopped()
    {
        var host = new InProcessHost((ILogger?)null);

        if (host.State != HostState.Pending)
        {
            throw new InvalidOperationException($"expected Pending before start, got {host.State}");
        }
        if (host.ExecutionMode != OfferingExecutionMode.InProcess)
        {
            throw new InvalidOperationException($"expected InProcess mode, got {host.ExecutionMode}");
        }

        await host.StartAsync(CancellationToken.None).ConfigureAwait(false);
        if (host.State != HostState.Running)
        {
            throw new InvalidOperationException($"expected Running after start, got {host.State}");
        }

        await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
        if (host.State != HostState.Stopped)
        {
            throw new InvalidOperationException($"expected Stopped after stop, got {host.State}");
        }
    }

    [GeneratedTest("Host.Execution/OutOfProcessHost-Start-Without-Configure-Is-Rejected", "global::Atelier.Framework.Host.Execution.Hosts.OutOfProcessHost")]
    public static async Task OutOfProcessHostStartWithoutProcessThrows()
    {
        var host = new OutOfProcessHost((ILogger?)null);

        try
        {
            await host.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException("expected start without a configured process to be rejected");
    }
}
