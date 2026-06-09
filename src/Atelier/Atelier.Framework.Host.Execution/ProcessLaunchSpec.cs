namespace Atelier.Framework.Host.Execution;

public sealed class ProcessLaunchSpec
{
    public string Executable { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = new();
    public string? WorkingDirectory { get; set; }
}
