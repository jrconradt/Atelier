using Atelier.Framework.Context;
namespace Atelier.Framework.Context.Extensions
{
    public static class ContextDataExtensions
    {
        public static IContext AddData(this IContext context, string key, string value)
        {
            var extension = context.Extensions.Get<DataBagExtension>();
            if (extension == null)
            {
                extension = new DataBagExtension();
                context.Extensions.Register(extension);
            }

            extension.Set(key, value);
            return context;
        }

        public static bool TryGetData(this IContext context, string key, out string? value)
        {
            var extension = context.Extensions.Get<DataBagExtension>();
            if (extension != null)
            {
                return extension.TryGet(key, out value);
            }
            value = null;
            return false;
        }

        public static string? GetData(this IContext context, string key)
        {
            var extension = context.Extensions.Get<DataBagExtension>();
            return extension?.Get(key);
        }

        public static IReadOnlyDictionary<string, string> GetAllData(this IContext context)
        {
            var extension = context.Extensions.Get<DataBagExtension>();
            return extension?.Data ?? new Dictionary<string, string>();
        }
    }
}
