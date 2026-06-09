using Atelier.Framework.Context;
namespace Atelier.Framework.Context
{
    public interface IContextAccessor
    {
        public IContext Current { get; }
        public void SetCurrent(IContext context);
    }
}
