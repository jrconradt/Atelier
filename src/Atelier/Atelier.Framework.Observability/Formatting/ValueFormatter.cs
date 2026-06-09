using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace Atelier.Framework.Observability.Formatting;

internal static class ValueFormatter
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false
    };

        public static string FormatValue(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is string str)
        {
            return Sanitize(str);
        }

        var type = value.GetType();

        if (value is DateTime dateTime)
        {
            return Sanitize(dateTime.ToString("O", CultureInfo.InvariantCulture));
        }

        if (value is TimeSpan timeSpan)
        {
            return Sanitize(timeSpan.ToString("c", CultureInfo.InvariantCulture));
        }

        if (value is Guid guid)
        {
            return Sanitize(guid.ToString(null, CultureInfo.InvariantCulture));
        }

        if (type.IsEnum)
        {
            return Sanitize(value.ToString());
        }

        if (value is IFormattable formattable)
        {
            return Sanitize(formattable.ToString(null, CultureInfo.InvariantCulture));
        }

        if (type.IsPrimitive || value is decimal)
        {
            return Sanitize(value.ToString());
        }

        if (value is IDictionary dictionary)
        {
            return FormatDictionary(dictionary);
        }

        if (value is IEnumerable enumerable and not string)
        {
            return FormatEnumerable(enumerable);
        }

        try
        {
            return JsonSerializer.Serialize(value, _jsonOptions);
        }
        catch
        {
            return Sanitize(value.ToString());
        }
    }

    private static string FormatEnumerable(IEnumerable enumerable)
        => "[" + string.Join(", ", enumerable.Cast<object?>().Select(FormatValue)) + "]";

    private static string FormatDictionary(IDictionary dictionary)
        => "{" + string.Join(", ", dictionary.Cast<DictionaryEntry>()
                .Select(e => $"{FormatValue(e.Key)}: {FormatValue(e.Value)}")) + "}";

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "null";
        }

        var result = new List<char>(value.Length);
        foreach (var ch in value)
        {
            if (char.IsControl(ch))
            {
                result.Add(' ');
            }
            else
            {
                result.Add(ch);
            }
        }

        return new string(result.ToArray());
    }
}
