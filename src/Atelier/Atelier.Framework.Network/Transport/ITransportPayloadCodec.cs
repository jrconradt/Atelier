namespace Atelier.Framework.Network.Transport
{
    public interface ITransportPayloadCodec
    {
        string ContentType { get; }

        byte[] Serialize<T>(T value);

        T Deserialize<T>(ReadOnlyMemory<byte> payload);
    }
}
