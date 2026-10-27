using Atelier.Framework.Primitives;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;

namespace Atelier.Framework.EventStream;

[Infrastructure(InfrastructureLifetime.Singleton)]
public partial class InMemoryHashRegistry : HashRegistryBase, IAtelier
{
}
