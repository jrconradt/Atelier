namespace Atelier.Framework.Context.Extensions
{
    public interface IContextExtension
    {
        public string ExtensionName { get; }

        public IContextExtension Clone();

        public bool ShouldPropagateToChildren { get; }
    }
}
