namespace Atelier.Framework.Context.Extensions
{
    public interface IContextExtensionRegistry
    {
        public void Register<TExtension>(TExtension extension) where TExtension : class, IContextExtension;

        public TExtension? Get<TExtension>() where TExtension : class, IContextExtension;

        public bool TryGet<TExtension>(out TExtension? extension) where TExtension : class, IContextExtension;

        public bool Has<TExtension>() where TExtension : class, IContextExtension;

        public void Remove<TExtension>() where TExtension : class, IContextExtension;

        public IEnumerable<IContextExtension> GetAll();
    }
}
