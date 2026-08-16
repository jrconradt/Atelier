using System.Collections.Concurrent;
using System.Diagnostics;
using Atelier.Build.Pipeline;

namespace Atelier.Build.Utils;

public sealed class ProcessExecutor
{
    private readonly BuildContext _context;

    public ProcessExecutor(BuildContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

        public Task<ProcessResult> ExecuteAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        ProcessOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return RunAsync(fileName, startInfo, options, cancellationToken);
    }

        public Task<ProcessResult> ExecuteAsync(
        string fileName,
        IEnumerable<string> argumentList,
        string workingDirectory,
        ProcessOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in argumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return RunAsync(fileName, startInfo, options, cancellationToken);
    }

        private async Task<ProcessResult> RunAsync(
        string fileName,
        ProcessStartInfo startInfo,
        ProcessOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= ProcessOptions.Default;

        using var process = new Process { StartInfo = startInfo };

        var outputLines = new ConcurrentQueue<string>();
        var errorLines = new ConcurrentQueue<string>();
        var outputCount = 0;
        var errorCount = 0;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            var index = Interlocked.Increment(ref outputCount);
            if (index <= options.MaxOutputLines)
            {
                outputLines.Enqueue(e.Data);
                options.OnOutputLine?.Invoke(e.Data);
            }
            else if (index == options.MaxOutputLines + 1)
            {
                outputLines.Enqueue($"... (output truncated after {options.MaxOutputLines} lines)");
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            var index = Interlocked.Increment(ref errorCount);
            if (index <= options.MaxOutputLines)
            {
                errorLines.Enqueue(e.Data);
                options.OnErrorLine?.Invoke(e.Data);
            }
            else if (index == options.MaxOutputLines + 1)
            {
                errorLines.Enqueue($"... (error output truncated after {options.MaxOutputLines} lines)");
            }
        };

        using var timeoutCts = options.Timeout.HasValue
            ? new CancellationTokenSource(options.Timeout.Value)
            : new CancellationTokenSource();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        try
        {
            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                await KillProcessTreeAsync(process).ConfigureAwait(false);

                throw new ProcessExecutionException(
                    $"Process '{fileName}' timed out after {options.Timeout}",
                    exitCode: -1,
                    standardOutputLines: outputLines.ToArray(),
                    standardErrorLines: errorLines.ToArray());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await KillProcessTreeAsync(process).ConfigureAwait(false);

                throw new ProcessExecutionException(
                    $"Process '{fileName}' was cancelled",
                    exitCode: -1,
                    standardOutputLines: outputLines.ToArray(),
                    standardErrorLines: errorLines.ToArray());
            }

            process.WaitForExit(3000);

            var exitCode = process.ExitCode;

            return new ProcessResult
            {
                ExitCode = exitCode,
                StandardOutputLines = outputLines.ToArray(),
                StandardErrorLines = errorLines.ToArray(),
                Success = exitCode == 0
            };
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new ProcessExecutionException(
                $"Command '{fileName}' not found or failed to start: {ex.Message}",
                exitCode: -1,
                standardOutputLines: System.Array.Empty<string>(),
                standardErrorLines: new[] { ex.Message });
        }
        catch (ProcessExecutionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ProcessExecutionException(
                $"Unexpected error executing '{fileName}': {ex.Message}",
                exitCode: -1,
                standardOutputLines: outputLines.ToArray(),
                standardErrorLines: errorLines.ToArray());
        }
    }

        private static async Task KillProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);

                using var killWaitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(killWaitCts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or OperationCanceledException)
        {
        }
    }
}

public sealed class ProcessOptions
{
        public TimeSpan? Timeout { get; init; }

        public int MaxOutputLines { get; init; } = 100_000;

        public Action<string>? OnOutputLine { get; init; }

        public Action<string>? OnErrorLine { get; init; }

        public static ProcessOptions Default => new();

        public static ProcessOptions WithTimeout(TimeSpan timeout) => new() { Timeout = timeout };

        public static ProcessOptions WithTimeoutAndCallbacks(
        TimeSpan timeout,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null) => new()
        {
            Timeout = timeout,
            OnOutputLine = onOutputLine,
            OnErrorLine = onErrorLine
        };
}

public sealed class ProcessResult
{
    public int ExitCode { get; init; }

    public IReadOnlyList<string> StandardOutputLines { get; init; } = System.Array.Empty<string>();
    public IReadOnlyList<string> StandardErrorLines { get; init; } = System.Array.Empty<string>();

    public string StandardOutput => string.Join("\n", StandardOutputLines);
    public string StandardError => string.Join("\n", StandardErrorLines);

    public bool Success { get; init; }
}

public sealed class ProcessExecutionException : Exception
{
    public int ExitCode { get; }
    public IReadOnlyList<string> StandardOutputLines { get; }
    public IReadOnlyList<string> StandardErrorLines { get; }

    public string StandardOutput => string.Join("\n", StandardOutputLines);
    public string StandardError => string.Join("\n", StandardErrorLines);

    public ProcessExecutionException(
        string message,
        int exitCode,
        IReadOnlyList<string> standardOutputLines,
        IReadOnlyList<string> standardErrorLines)
        : base(message)
    {
        ExitCode = exitCode;
        StandardOutputLines = standardOutputLines;
        StandardErrorLines = standardErrorLines;
    }
}
