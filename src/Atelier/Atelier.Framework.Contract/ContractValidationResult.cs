using Atelier.Framework.Attributes;

namespace Atelier.Framework.Contract;

[Contract("ContractValidationResult", Version = "1.0", Namespace = "Framework.Contract")]
public class ContractValidationResult
{
        public List<ValidationError> Errors { get; set; } = new();

        public bool IsValid { get; set; }

        public string? ErrorMessage { get; set; }

        public string? ErrorCode { get; set; }

        protected ContractValidationResult(
        bool isValid,
        string? errorMessage = null,
        string? errorCode = null)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
    }

        public static ContractValidationResult Success() =>
        new(true);

        public static ContractValidationResult Failure(params ValidationError[] errors) =>
        new(
            false,
            string.Join("; ", errors.Select(e => e.Message)),
            "VALIDATION_FAILED")
        {
            Errors = errors.ToList()
        };
}

public class ValidationError
{
        public required string Field { get; set; }

        public required string Message { get; set; }

        public string? Code { get; set; }
}
