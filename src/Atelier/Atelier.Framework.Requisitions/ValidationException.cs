namespace Atelier.Framework.Requisitions;

public class ValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ValidationException(IEnumerable<string> errors)
        : base($"Validation failed: {string.Join(", ", errors)}")
    {
        Errors = errors.ToList();
    }
}
