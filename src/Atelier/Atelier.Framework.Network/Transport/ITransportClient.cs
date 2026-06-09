
namespace Atelier.Framework.Network.Transport
{
    public interface ITransportClient : IDisposable
    {
        public bool IsConnected { get; }
        public Task ConnectAsync(CancellationToken cancellationToken = default);
        public Task DisconnectAsync(CancellationToken cancellationToken = default);
        public Task<ITransportMessage?> SendAsync(ITransportMessage message, CancellationToken cancellationToken = default);
        public Task<ITransportMessage?> ReceiveAsync(CancellationToken cancellationToken = default);
    }
}
