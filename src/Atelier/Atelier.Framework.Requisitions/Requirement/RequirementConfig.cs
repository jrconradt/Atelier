namespace Atelier.Framework.Requisitions.Requirement;

public class RequirementConfig
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RequirementType Type { get; set; } = RequirementType.Service;
    public string ResourceIdentifier { get; set; } = string.Empty;
    public Dictionary<string, object> ValidationCriteria { get; set; } = new();
    public bool IsOptional { get; set; } = false;
    public string? ErrorMessage { get; set; }
    public TimeSpan? Timeout { get; set; }
    public int RetryCount { get; set; } = 0;
    public Dictionary<string, string> Tags { get; set; } = new();
    public Dictionary<string, object> Properties { get; set; } = new();
}
