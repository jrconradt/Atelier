using Templar.Rendering;
using G = Atelier.Framework.Test.Generators.Templates.Wiring;

namespace Atelier.Framework.Test.Generators;

internal static class WiringTestEmitter
{
    public static IEnumerable<Compositor> Emit(ConsumerMetadata m)
    {
        if (m.RequisiteFields.Count == 0)
        {
            yield break;
        }

        if (!m.GeneratorEmitsConstructor)
        {
            yield return FixtureRequired(m, "DI-Wiring/Ctor-Exists", "Test_DiWiring_CtorExists_",
                "Class has a user-declared constructor; generator did not synthesize one. Provide a TestFixtures.Register fixture or remove the hand-written ctor.");
            yield return FixtureRequired(m, "DI-Wiring/All-Fields-Wired", "Test_DiWiring_AllFieldsWired_",
                "Class has a user-declared constructor; generator did not wire [Requisite] fields. Provide a TestFixtures.Register fixture or remove the hand-written ctor.");
            yield break;
        }

        yield return new G.CtorExists
        {
            Target = m.FullyQualifiedName,
            ClassName = m.ClassName,
            Fqn = m.FullyQualifiedName,
            Arity = m.ExpectedCtorArity,
        };

        yield return new G.AllFieldsWired
        {
            Target = m.FullyQualifiedName,
            ClassName = m.ClassName,
            Fqn = m.FullyQualifiedName,
            Arity = m.ExpectedCtorArity,
            FieldNames = Sequence.Lines(m.RequisiteFields.Select(f => (Compositor)new G.FieldNameLine { Name = f.Name })),
        };
    }

    private static Compositor FixtureRequired(ConsumerMetadata m, string invariant, string methodPrefix, string reason) =>
        new G.FixtureRequired
        {
            Invariant = invariant,
            Target = m.FullyQualifiedName,
            MethodName = methodPrefix + m.ClassName,
            Reason = EscapeForString(reason),
        };

    private static string EscapeForString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
