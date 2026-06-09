using Atelier.Framework.Attributes;

namespace Atelier.Framework.Contract;

[Contract("ContractMetadata", Version = "1.0", Namespace = "Framework.Contract")]
public sealed class ContractMetadata
{
    public required string Name { get; set; }

    public required string Version { get; set; }

    public string? Namespace { get; set; }

    public required Type ContractType { get; set; }

    public bool IsBackwardCompatible { get; set; }

    public Dictionary<string, string> Properties { get; set; } = new();

    public List<string> RequiredFields { get; set; } = new();

    public List<string> OptionalFields { get; set; } = new();

    public string FullQualifiedName =>
        string.IsNullOrEmpty(Namespace)
            ? $"{Name}:{Version}"
            : $"{Namespace}.{Name}:{Version}";
}
