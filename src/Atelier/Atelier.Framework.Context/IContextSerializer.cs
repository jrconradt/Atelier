using Atelier.Framework.Context;

namespace Atelier.Framework.Context
{
    public interface IContextSerializer
    {
        public string Serialize(IContext context);
        public IContext Deserialize(string serialized);
        public bool TryDeserialize(string serialized, out IContext? context);
    }
}
