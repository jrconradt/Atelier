using Atelier.Framework.Attributes;

namespace Atelier.Framework.Offering;

[ContractAttribute(
    "OfferingInstanceResponse",
    Version = "1.0",
    Namespace = "Framework.Offering")]
public class OfferingInstanceResponse
{
    public bool IsSuccess { get; set; }

    public OfferingInstanceDescriptor? Descriptor { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ErrorCode { get; set; }

    public static OfferingInstanceResponse Success(OfferingInstanceDescriptor descriptor)
    {
        return new OfferingInstanceResponse
        {
            IsSuccess = true,
            Descriptor = descriptor
        };
    }

    public static OfferingInstanceResponse Failure(string errorMessage, string errorCode)
    {
        return new OfferingInstanceResponse
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode
        };
    }
}
