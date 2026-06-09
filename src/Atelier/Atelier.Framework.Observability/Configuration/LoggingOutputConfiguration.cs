
namespace Atelier.Framework.Observability.Configuration;

public class LoggingOutputConfiguration
{
    public string OutputType { get; set; } = string.Empty;
    public Dictionary<string, object> Configuration { get; set; } = new();
    public string Formatter { get; set; } = "json";
    public Dictionary<string, object> FormatterOptions { get; set; } = new();
    public List<string> Filters { get; set; } = new();
}





