using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Contract;

public interface IContractSerializer
{
    public byte[] Serialize<T>(T contract) where T : class;

    public byte[] Serialize(
        object contract,
        Type contractType);

    public T? Deserialize<T>(byte[] data) where T : class;

    public object? Deserialize(
        byte[] data,
        Type contractType);

    public Outcome<SerializedContract> SerializeWithMetadata<T>(T contract) where T : class;

    public Outcome<object?> DeserializeWithMetadata(SerializedContract serialized);

    public Outcome<object?> DeserializeWithMetadata(
        SerializedContract serialized,
        Type? expectedType);
}
