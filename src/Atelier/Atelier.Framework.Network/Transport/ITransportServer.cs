using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Network.Transport
{
    public interface ITransportServer : IDisposable
    {
        public bool IsRunning { get; }
        public Task StartAsync(CancellationToken cancellationToken = default);
        public Task StopAsync(CancellationToken cancellationToken = default);
        public void RegisterMessageHandler(Func<ITransportMessage, CancellationToken, Task<Outcome>> handler);
    }
}
