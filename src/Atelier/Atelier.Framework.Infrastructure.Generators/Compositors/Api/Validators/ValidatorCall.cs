using Templar.Rendering;

namespace Atelier.Framework.Infrastructure.Generators.Compositors.Api.Validators;

public abstract class ValidatorCall : Compositor
{
    public required string ParamName { get; init; }
}
