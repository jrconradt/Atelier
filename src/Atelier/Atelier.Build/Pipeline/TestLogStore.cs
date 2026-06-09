using Atelier.Build.Utils;
using Spectre.Console;

namespace Atelier.Build.Pipeline;

public sealed class TestLogStore
{
    private readonly BuildContext _context;

    public TestLogStore(BuildContext context)
    {
        _context = context;
    }

    public async Task<string> WriteTestLogAsync(
        string subsystemName,
        string projectName,
        string output,
        int exitCode,
        string verbosity)
    {
        Directory.CreateDirectory(_context.LogDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var logFileName = $"test-{subsystemName}-{projectName}-{timestamp}.log";
        var logPath = Path.Combine(_context.LogDirectory, logFileName);

        var logContent = $"""
            ╔══════════════════════════════════════════════════════════════╗
            ║  SMASH TEST LOG                                              ║
            ╠══════════════════════════════════════════════════════════════╣
            ║  Subsystem: {subsystemName}
            ║  Project: {projectName}
            ║  Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
            ║  Exit Code: {exitCode}
            ║  Status: {(exitCode == 0 ? "PASSED" : "FAILED")}
            ║  Verbosity: {verbosity}
            ╚══════════════════════════════════════════════════════════════╝

            {output}
            """;

        await File.WriteAllTextAsync(logPath, logContent).ConfigureAwait(false);

        var latestLogPath = Path.Combine(_context.LogDirectory, $"test-{subsystemName}-latest.log");
        await File.WriteAllTextAsync(latestLogPath, logContent).ConfigureAwait(false);

        EnforceTestLogRetention(subsystemName);

        if (_context.Verbose)
        {
            AnsiConsole.MarkupLine($"  [dim]Test log written to: {logPath}[/]");
        }

        return logPath;
    }

    private void EnforceTestLogRetention(string subsystemName)
    {
        var logFiles = Directory.GetFiles(_context.LogDirectory, $"test-{subsystemName}-*.log")
            .Where(f => !f.EndsWith($"test-{subsystemName}-latest.log"))
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime)
            .ToList();

        if (logFiles.Count > BuildContext.MAX_LOG_FILES)
        {
            foreach (var oldLog in logFiles.Skip(BuildContext.MAX_LOG_FILES))
            {
                try
                {
                    oldLog.Delete();
                    if (_context.Verbose)
                    {
                        AnsiConsole.MarkupLine($"  [dim]Deleted old test log: {oldLog.Name}[/]");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (_context.Verbose)
                    {
                        AnsiConsole.MarkupLine($"  [dim]Could not delete old test log {oldLog.Name.EscapeMarkup()}: {ex.Message.EscapeMarkup()}[/]");
                    }
                }
            }
        }
    }
}
