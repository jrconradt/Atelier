using Microsoft.CodeAnalysis;
using Templar.Rendering;
using G = Atelier.Framework.Network.Generators.Templates.Transport;

namespace Atelier.Framework.Network.Transport.CodeGen;

internal sealed class InProcessClientGenerator : TransportClientGenerator
{
    public InProcessClientGenerator(INamedTypeSymbol iface) : base(iface, "InProcessTransport") { }

    public override string Variant => "InProcessTransport";

    protected override IEnumerable<string> ExtraUsings => new[]
    {
        "Atelier.Framework.Network.Transport.InProcess",
    };

    protected override Compositor BuildBody() => new G.InProcess.Transport
    {
        ClassName = ClassName,
        IfaceName = Iface.Name,
        Methods = ClientMethods(),
    };
}
