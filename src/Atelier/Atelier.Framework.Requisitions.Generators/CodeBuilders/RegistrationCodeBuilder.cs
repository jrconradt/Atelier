using Templar.Rendering;
using Templar.Presets;
using G = Atelier.Framework.Requisitions.Generators.Templates.Registration;
using L = Atelier.Framework.Requisitions.Generators.Compositors.Registration.Lifecycles;

namespace Atelier.Framework.Generators.Requisition;

internal class RegistrationCodeBuilder
{
    private readonly List<RegistrationInfo> _registrations;

    public RegistrationCodeBuilder(List<RegistrationInfo> registrations)
    {
        _registrations = registrations;
    }

    public string Build()
    {
        var namespaces = _registrations
            .Select(r => r.Namespace)
            .Distinct()
            .OrderBy(ns => ns);

        var usings = new[]
        {
            "System",
            "Atelier.Framework.Requisitions",
        }.Concat(namespaces);

        var orderedRegistrations = _registrations
            .OrderBy(r => r.FullyQualifiedTypeName, StringComparer.Ordinal);

        var body = new G.RegistrationFile
        {
            Registrations = Sequence.BlankLines(orderedRegistrations.Select(BuildRegistration)),
        };

        return new CSharpFile
        {
            Namespace = "Atelier.Framework.Requisitions",
            Usings = usings,
            Body = body.Render(),
        }.Render();
    }

    private static Compositor BuildRegistration(RegistrationInfo r) =>
        new G.RegistrationBlock
        {
            TypeName = r.TypeName,
            FullTypeName = r.FullTypeName,
            LifecycleMethod = LifecycleFor(r.Lifecycle),
        };

    private static Compositor LifecycleFor(LifecycleType lifecycle) => lifecycle switch
    {
        LifecycleType.Singleton => new L.AddSingletonMethod(),
        LifecycleType.Scoped => new L.AddScopedMethod(),
        LifecycleType.Transient => new L.AddTransientMethod(),
        _ => new L.AddTransientMethod(),
    };
}
