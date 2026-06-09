using Microsoft.CodeAnalysis;
using Templar.Rendering;
using Templar.Presets;
using Atelier.Framework.Generators.Requisition;

namespace Atelier.Framework.Network.Transport.CodeGen;

internal abstract class TransportServerGenerator
{
    private static readonly string[] BASE_USINGS =
    {
        "System",
        "System.Text.Json",
        "System.Threading",
        "System.Threading.Tasks",
        "Atelier.Framework.Network.Transport",
        "Atelier.Framework.Outcomes",
    };

    protected INamedTypeSymbol Iface { get; }
    protected string ClassName { get; }

    public abstract string Variant { get; }

    protected TransportServerGenerator(INamedTypeSymbol iface, string classSuffix)
    {
        Iface = iface;
        ClassName = SymbolNaming.ImplName(iface.Name) + classSuffix;
    }

    protected abstract Compositor BuildBody();
    protected abstract IEnumerable<string> ExtraUsings { get; }

    public string Render() => new CSharpFile
    {
        Namespace = Iface.ContainingNamespace.ToDisplayString(),
        Usings = BASE_USINGS.Concat(ExtraUsings).ToArray(),
        Body = BuildBody().Render(),
    }.Render();

    protected IEnumerable<IMethodSymbol> OrdinaryMethods =>
        Iface.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary);

    protected Sequence ServerCases() => Sequence.Lines(OrdinaryMethods.Select(ServerCaseEmitter.Emit));
}
