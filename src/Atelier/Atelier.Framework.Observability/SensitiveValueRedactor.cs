using System.Collections;
using System.Text.RegularExpressions;

namespace Atelier.Framework.Observability
{
    public static class SensitiveValueRedactor
    {
        public const string RedactedPlaceholder = "***";
        public const string RedactedTextPlaceholder = "[REDACTED]";

        private static readonly string[] SensitiveKeyTokens =
        [
            "secret",
            "password",
            "passwd",
            "token",
            "apikey",
            "api_key",
            "credential",
            "privatekey",
            "private_key",
            "connectionstring",
            "email",
            "phone",
            "ssn",
            "socialsecurity",
            "dateofbirth",
            "firstname",
            "lastname",
            "fullname",
            "givenname",
            "surname",
            "streetaddress",
            "mailingaddress",
            "homeaddress",
            "emailaddress",
            "creditcard",
            "cardnumber",
            "userid",
            "tenantid",
            "sessionid"
        ];

        private static readonly Regex LabeledSecretPattern =
            new(
                "(?i)(password|passwd|pwd|secret|token|apikey|api[_-]?key|authorization|bearer|access[_-]?key|private[_-]?key|connectionstring|conn[_-]?str)\\s*[=:]\\s*\\S+",
                RegexOptions.Compiled);

        private static readonly Regex JwtPattern =
            new(
                "eyJ[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+",
                RegexOptions.Compiled);

        private static readonly Regex UriCredentialPattern =
            new(
                "(?i)([a-z][a-z0-9+.-]*://)[^/@\\s:]+:[^/@\\s]+@",
                RegexOptions.Compiled);

        private static readonly Regex EmailPattern =
            new(
                "[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}",
                RegexOptions.Compiled);

        private static readonly Regex SsnPattern =
            new(
                "\\b\\d{3}-\\d{2}-\\d{4}\\b",
                RegexOptions.Compiled);

        private static readonly Regex CreditCardPattern =
            new(
                "\\b(?:\\d[ -]?){13,19}\\b",
                RegexOptions.Compiled);

        public static bool IsSensitiveKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            foreach (var token in SensitiveKeyTokens)
            {
                if (key.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static string RedactText(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var result = LabeledSecretPattern.Replace(text, "$1=[REDACTED]");
            result = JwtPattern.Replace(result, RedactedTextPlaceholder);
            result = UriCredentialPattern.Replace(result, "$1[REDACTED]@");
            result = EmailPattern.Replace(result, RedactedTextPlaceholder);
            result = SsnPattern.Replace(result, RedactedTextPlaceholder);
            result = CreditCardPattern.Replace(result, RedactedTextPlaceholder);
            return result;
        }

        public static IDictionary<string, object> RedactInPlace(IDictionary<string, object> values)
        {
            if (values == null)
            {
                return new Dictionary<string, object>();
            }

            var stack = new Stack<object>();
            stack.Push(values);

            while (stack.Count > 0)
            {
                var container = stack.Pop();

                if (container is IDictionary<string, object> dictionary)
                {
                    var keys = dictionary.Keys.ToList();

                    foreach (var key in keys)
                    {
                        if (IsSensitiveKey(key))
                        {
                            dictionary[key] = RedactedPlaceholder;
                            continue;
                        }

                        dictionary[key] = RedactValue(dictionary[key], stack);
                    }

                    continue;
                }

                if (container is IList list)
                {
                    for (var index = 0; index < list.Count; index++)
                    {
                        list[index] = RedactValue(list[index] ?? string.Empty, stack);
                    }
                }
            }

            return values;
        }

        private static object RedactValue(object value, Stack<object> stack)
        {
            if (value is string text)
            {
                return RedactText(text);
            }

            if (value is IDictionary<string, object> nested)
            {
                stack.Push(nested);
                return nested;
            }

            if (value is IDictionary rawNested)
            {
                var converted = new Dictionary<string, object>();
                foreach (DictionaryEntry entry in rawNested)
                {
                    converted[entry.Key?.ToString() ?? string.Empty] = entry.Value ?? string.Empty;
                }
                stack.Push(converted);
                return converted;
            }

            if (value is IList existingList)
            {
                stack.Push(existingList);
                return existingList;
            }

            if (value is IEnumerable enumerable
                && value is not string)
            {
                var converted = new List<object>();
                foreach (var element in enumerable)
                {
                    converted.Add(element ?? string.Empty);
                }
                stack.Push(converted);
                return converted;
            }

            var type = value.GetType();

            if (type.IsPrimitive
                || value is decimal
                || value is DateTime
                || value is DateTimeOffset
                || value is TimeSpan
                || value is Guid
                || type.IsEnum)
            {
                return value;
            }

            return RedactText(value.ToString() ?? string.Empty);
        }
    }
}
