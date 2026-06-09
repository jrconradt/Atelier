using Templar.Rendering;

namespace Atelier.Framework.Infrastructure.Generators.Compositors.Validation.Validators;

public abstract class ValidationStatement : Compositor
{
    public required string ParamName { get; init; }
}
