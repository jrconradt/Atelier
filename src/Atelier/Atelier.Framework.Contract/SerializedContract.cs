namespace Atelier.Framework.Contract;

public class SerializedContract
{
    public required string ContractName { get; set; }

    public required string ContractVersion { get; set; }

    public string? ContractNamespace { get; set; }

    public required byte[] Payload { get; set; }

    public required string SerializationFormat { get; set; }

    public DateTime SerializedAt { get; set; } = DateTime.UtcNow;
}
