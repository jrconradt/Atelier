using Templar.Rendering;

namespace Atelier.Framework.Requisitions.Generators.Compositors.Helper.HelperSignatures;

internal abstract class HelperSignature : Compositor
{
    public required string TypeName { get; init; }
}
