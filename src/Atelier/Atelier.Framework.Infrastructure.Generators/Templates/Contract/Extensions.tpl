public static class {{ className }}ContractValidationExtensions
{
    public static global::System.Collections.Generic.IEnumerable<global::System.ComponentModel.DataAnnotations.ValidationResult> Validate{{ typeParams }}(this {{ targetType }} __target){{ constraints }}
    {
        var results = new global::System.Collections.Generic.List<global::System.ComponentModel.DataAnnotations.ValidationResult>();
        if (__target is null)
        {
            results.Add(new global::System.ComponentModel.DataAnnotations.ValidationResult("Instance is null"));
            return results;
        }

        {{ propertyValidations }}

        return results;
    }

    public static bool IsValid{{ typeParams }}(this {{ targetType }} __target){{ constraints }}
    {
        return !global::System.Linq.Enumerable.Any(Validate(__target));
    }

    public static void EnsureValid{{ typeParams }}(this {{ targetType }} __target){{ constraints }}
    {
        var validationResults = global::System.Linq.Enumerable.ToList(Validate(__target));
        if (global::System.Linq.Enumerable.Any(validationResults))
        {
            var errorMessage = string.Join("; ", global::System.Linq.Enumerable.Select(validationResults, r => r.ErrorMessage));
            throw new global::System.ComponentModel.DataAnnotations.ValidationException($"Validation failed for {{ className }}: {errorMessage}");
        }
    }
}
