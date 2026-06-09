using System.Collections;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Observability;

public static class SensitiveValueRedactorBehaviorTests
{
    [GeneratedTest("Observability/Redactor-Masks-Labeled-Secret", "global::Atelier.Framework.Observability.SensitiveValueRedactor")]
    public static void RedactTextMasksLabeledSecretAssignment()
    {
        var redacted = SensitiveValueRedactor.RedactText("password=hunter2");

        if (redacted.Contains("hunter2"))
        {
            throw new InvalidOperationException($"labeled secret survived redaction: {redacted}");
        }
        if (!redacted.Contains(SensitiveValueRedactor.RedactedTextPlaceholder))
        {
            throw new InvalidOperationException($"redaction placeholder missing: {redacted}");
        }
    }

    [GeneratedTest("Observability/Redactor-Masks-Jwt", "global::Atelier.Framework.Observability.SensitiveValueRedactor")]
    public static void RedactTextMasksJwt()
    {
        var jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36";
        var redacted = SensitiveValueRedactor.RedactText($"token is {jwt}");

        if (redacted.Contains(jwt))
        {
            throw new InvalidOperationException($"jwt survived redaction: {redacted}");
        }
    }

    [GeneratedTest("Observability/Redactor-Masks-Uri-Credential", "global::Atelier.Framework.Observability.SensitiveValueRedactor")]
    public static void RedactTextMasksUriCredential()
    {
        var redacted = SensitiveValueRedactor.RedactText("postgres://admin:s3cr3t@db.internal:5432");

        if (redacted.Contains("s3cr3t"))
        {
            throw new InvalidOperationException($"uri password survived redaction: {redacted}");
        }
    }

    [GeneratedTest("Observability/Redactor-Masks-Email", "global::Atelier.Framework.Observability.SensitiveValueRedactor")]
    public static void RedactTextMasksEmail()
    {
        var redacted = SensitiveValueRedactor.RedactText("contact jane.doe@example.com today");

        if (redacted.Contains("jane.doe@example.com"))
        {
            throw new InvalidOperationException($"email survived redaction: {redacted}");
        }
    }

    [GeneratedTest("Observability/Redactor-Masks-Ssn", "global::Atelier.Framework.Observability.SensitiveValueRedactor")]
    public static void RedactTextMasksSocialSecurityNumber()
    {
        var redacted = SensitiveValueRedactor.RedactText("ssn 123-45-6789 on file");

        if (redacted.Contains("123-45-6789"))
        {
            throw new InvalidOperationException($"ssn survived redaction: {redacted}");
        }
    }

    [GeneratedTest("Observability/Redactor-Masks-Credit-Card", "global::Atelier.Framework.Observability.SensitiveValueRedactor")]
    public static void RedactTextMasksCreditCardNumber()
    {
        var redacted = SensitiveValueRedactor.RedactText("card 4111 1111 1111 1111 charged");

        if (redacted.Contains("4111 1111 1111 1111"))
        {
            throw new InvalidOperationException($"credit card survived redaction: {redacted}");
        }
    }

    [GeneratedTest("Observability/Redactor-Sensitive-Key-Categories-Match", "global::Atelier.Framework.Observability.SensitiveValueRedactor")]
    public static void IsSensitiveKeyMatchesEachCategory()
    {
        string[] sensitive =
        [
            "Secret",
            "UserPassword",
            "AccessToken",
            "ApiKey",
            "Credential",
            "ConnectionString",
            "EmailAddress",
            "PhoneNumber",
            "Ssn",
            "DateOfBirth",
            "FirstName",
            "StreetAddress",
            "CreditCard",
            "CardNumber"
        ];

        foreach (var key in sensitive)
        {
            if (!SensitiveValueRedactor.IsSensitiveKey(key))
            {
                throw new InvalidOperationException($"expected '{key}' to be sensitive");
            }
        }
    }

    [GeneratedTest("Observability/Redactor-Benign-Key-Not-Sensitive", "global::Atelier.Framework.Observability.SensitiveValueRedactor")]
    public static void IsSensitiveKeyRejectsBenignKeys()
    {
        string[] benign =
        [
            "Count",
            "OrderId",
            "Status",
            "DurationMs"
        ];

        foreach (var key in benign)
        {
            if (SensitiveValueRedactor.IsSensitiveKey(key))
            {
                throw new InvalidOperationException($"benign key '{key}' was treated as sensitive");
            }
        }
    }

    [GeneratedTest("Observability/Redactor-Sensitive-Key-Wins-Over-Value", "global::Atelier.Framework.Observability.SensitiveValueRedactor")]
    public static void RedactInPlaceMasksSensitiveKeyValue()
    {
        var values = new Dictionary<string, object>
        {
            ["Password"] = "anything"
        };

        SensitiveValueRedactor.RedactInPlace(values);

        if ((string)values["Password"] != SensitiveValueRedactor.RedactedPlaceholder)
        {
            throw new InvalidOperationException($"sensitive key value not masked: {values["Password"]}");
        }
    }

    [GeneratedTest("Observability/Redactor-Recurses-Nested-Typed-Dictionary", "global::Atelier.Framework.Observability.SensitiveValueRedactor")]
    public static void RedactInPlaceRecursesNestedTypedDictionary()
    {
        var nested = new Dictionary<string, object>
        {
            ["Note"] = "reach me at jane.doe@example.com"
        };
        var values = new Dictionary<string, object>
        {
            ["Inner"] = nested
        };

        SensitiveValueRedactor.RedactInPlace(values);

        var resultInner = (IDictionary<string, object>)values["Inner"];
        if (((string)resultInner["Note"]).Contains("jane.doe@example.com"))
        {
            throw new InvalidOperationException($"nested dictionary email survived: {resultInner["Note"]}");
        }
    }

    [GeneratedTest("Observability/Redactor-Recurses-Raw-Dictionary", "global::Atelier.Framework.Observability.SensitiveValueRedactor")]
    public static void RedactInPlaceConvertsAndRecursesRawDictionary()
    {
        IDictionary raw = new Hashtable
        {
            ["Note"] = "ssn 123-45-6789"
        };
        var values = new Dictionary<string, object>
        {
            ["Inner"] = raw
        };

        SensitiveValueRedactor.RedactInPlace(values);

        var resultInner = (IDictionary<string, object>)values["Inner"];
        if (((string)resultInner["Note"]).Contains("123-45-6789"))
        {
            throw new InvalidOperationException($"raw dictionary ssn survived: {resultInner["Note"]}");
        }
    }

    [GeneratedTest("Observability/Redactor-Recurses-List-Elements", "global::Atelier.Framework.Observability.SensitiveValueRedactor")]
    public static void RedactInPlaceRecursesListElements()
    {
        var values = new Dictionary<string, object>
        {
            ["Recipients"] = new List<object> { "jane.doe@example.com", "fine text" }
        };

        SensitiveValueRedactor.RedactInPlace(values);

        var list = (IList)values["Recipients"];
        if (((string)list[0]!).Contains("jane.doe@example.com"))
        {
            throw new InvalidOperationException($"list email element survived: {list[0]}");
        }
        if ((string)list[1]! != "fine text")
        {
            throw new InvalidOperationException($"benign list element altered: {list[1]}");
        }
    }

    [GeneratedTest("Observability/Redactor-Does-Not-Over-Redact-Benign", "global::Atelier.Framework.Observability.SensitiveValueRedactor")]
    public static void RedactInPlaceLeavesBenignValueIntact()
    {
        var values = new Dictionary<string, object>
        {
            ["OrderSummary"] = "3 items shipped",
            ["Count"] = 42
        };

        SensitiveValueRedactor.RedactInPlace(values);

        if ((string)values["OrderSummary"] != "3 items shipped")
        {
            throw new InvalidOperationException($"benign string was over-redacted: {values["OrderSummary"]}");
        }
        if (values["Count"] is not int count
            || count != 42)
        {
            throw new InvalidOperationException($"benign numeric value was altered: {values["Count"]}");
        }
    }
}
