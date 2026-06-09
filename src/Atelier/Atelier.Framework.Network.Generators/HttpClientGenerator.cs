using Microsoft.CodeAnalysis;
using Templar.Rendering;
using G = Atelier.Framework.Network.Generators.Templates.Transport;

namespace Atelier.Framework.Network.Transport.CodeGen;

internal sealed class HttpClientGenerator : TransportClientGenerator
{
    public HttpClientGenerator(INamedTypeSymbol iface) : base(iface, "HttpClient") { }

    public override string Variant => "HttpClient";

    protected override IEnumerable<string> ExtraUsings => new[]
    {
        "System.Net.Http",
        "Atelier.Framework.Network.Transport.Http",
    };

    protected override Compositor BuildBody() => new G.Http.Client
    {
        ClassName = ClassName,
        IfaceName = Iface.Name,
        Methods = ClientMethods(),
    };
}
