namespace Atelier.Framework.Observability.Configuration;

public class LoggingConfiguration
{
    public List<LoggingOutputConfiguration> Outputs { get; set; } = new();
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
    public Dictionary<string, object> Properties { get; set; } = new();
}






