using Templar.Rendering;

namespace Atelier.Build.Generation;

internal sealed class Verbatim : Compositor
{
    public required string Text { get; init; }
    protected override string Structure => Text;
    public override string Render() => Text;
}
