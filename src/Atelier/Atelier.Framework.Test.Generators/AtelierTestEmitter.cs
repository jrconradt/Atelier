using Templar.Rendering;
using G = Atelier.Framework.Test.Generators.Templates.Atelier;

namespace Atelier.Framework.Test.Generators;

internal static class AtelierTestEmitter
{
    public static IEnumerable<Compositor> Emit(ConsumerMetadata m)
    {
        if (!m.ImplementsIAtelier)
        {
            yield break;
        }

        var target = m.FullyQualifiedName;

        yield return new G.ObserveSurface { Target = target, ClassName = m.ClassName, Fqn = m.FullyQualifiedName };
        yield return new G.LoggerSurface { Target = target, ClassName = m.ClassName, Fqn = m.FullyQualifiedName };

        if (m.ExpectedCtorArity == 0)
        {
            yield break;
        }

        if (!m.GeneratorEmitsConstructor)
        {
            yield return new G.LoggerWiredFixture { Target = target, ClassName = m.ClassName };
            yield break;
        }

        yield return new G.LoggerWired
        {
            Target = target,
            ClassName = m.ClassName,
            Fqn = m.FullyQualifiedName,
            Arity = m.ExpectedCtorArity,
        };
    }
}
