using Docker.DotNet;

namespace Atelier.Framework.Host.Execution;

public interface IDockerClientProvider
{
    public IDockerClient Client { get; }
}
