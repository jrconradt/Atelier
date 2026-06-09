using Microsoft.CodeAnalysis;
using Templar.Rendering;
using G = Atelier.Framework.Network.Generators.Templates.Transport;

namespace Atelier.Framework.Network.Transport.CodeGen;

internal sealed class HttpServerGenerator : TransportServerGenerator
{
    public HttpServerGenerator(INamedTypeSymbol iface) : base(iface, "HttpServer") { }

    public override string Variant => "HttpServer";

    protected override IEnumerable<string> ExtraUsings => new[]
    {
        "Atelier.Framework.Network.Transport.Http",
    };

    protected override Compositor BuildBody() => new G.Http.Server
    {
        ClassName = ClassName,
        IfaceName = Iface.Name,
        Cases = ServerCases(),
    };
}
