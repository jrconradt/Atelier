using Templar.Rendering;
using G = Atelier.Framework.Test.Generators.Templates.Lifecycle;

namespace Atelier.Framework.Test.Generators;

internal static class LifecycleTestEmitter
{
    public static IEnumerable<Compositor> Emit(ConsumerMetadata m)
    {
        if (!m.IsProduct)
        {
            yield break;
        }
        if (m.RequisiteFields.Count == 0)
        {
            yield break;
        }

        yield return new G.ProductLifecycle
        {
            Target = m.FullyQualifiedName,
            ClassName = m.ClassName,
            Fqn = m.FullyQualifiedName,
            Arity = m.ExpectedCtorArity,
        };
    }
}
