using Atelier.Framework.Context;
namespace Atelier.Framework.Context.Extensions
{
    public static class ContextCompilationExtensions
    {
        public static IContext AddCompileTimeType(this IContext context, string key, Type type)
        {
            if (!context.IsCompileTime)
            {
                throw new InvalidOperationException("Cannot add compile-time types to a runtime context");
            }

            var extension = context.Extensions.Get<CompilationContextExtension>();
            if (extension == null)
            {
                extension = new CompilationContextExtension();
                context.Extensions.Register(extension);
            }

            extension.AddType(key, type);
            return context;
        }

        public static Type? GetCompileTimeType(this IContext context, string key)
        {
            var extension = context.Extensions.Get<CompilationContextExtension>();
            return extension?.GetType(key);
        }

        public static bool HasCompileTimeType(this IContext context, string key)
        {
            var extension = context.Extensions.Get<CompilationContextExtension>();
            return extension?.HasType(key) ?? false;
        }

        public static IReadOnlyDictionary<string, Type> GetAllCompileTimeTypes(this IContext context)
        {
            var extension = context.Extensions.Get<CompilationContextExtension>();
            return extension?.CompileTimeTypes ?? new Dictionary<string, Type>();
        }
    }
}
