using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atelier.Framework.Properties;

public class TypedPropertyBagJsonConverter : JsonConverter<TypedPropertyBag>
{
    public override TypedPropertyBag Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var bag = new TypedPropertyBag();

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object");
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return bag;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name");
            }

            var propertyName = reader.GetString();
            if (string.IsNullOrEmpty(propertyName))
            {
                throw new JsonException("Property name cannot be null or empty");
            }

            reader.Read();

            var value = ReadValue(ref reader);
            if (value != null)
            {
                bag.Set(propertyName, value);
            }
        }

        throw new JsonException("Unexpected end of JSON");
    }

    private object? ReadValue(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
            case JsonTokenType.Number:
            case JsonTokenType.True:
            case JsonTokenType.False:
            case JsonTokenType.Null:
            case JsonTokenType.StartObject:
            case JsonTokenType.StartArray:
            {
                var element = JsonElement.ParseValue(ref reader);
                return MaterializeElement(element);
            }
            default:
            {
                throw new JsonException($"Unsupported token type: {reader.TokenType}");
            }
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        TypedPropertyBag value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var kvp in value.GetRedacted())
        {
            writer.WritePropertyName(kvp.Key);
            WriteTypedValue(writer, kvp.Value, options);
        }

        writer.WriteEndObject();
    }

    private const string BOOLEAN_TOKEN = "Boolean";
    private const string CHAR_TOKEN = "Char";
    private const string SBYTE_TOKEN = "SByte";
    private const string BYTE_TOKEN = "Byte";
    private const string INT16_TOKEN = "Int16";
    private const string UINT16_TOKEN = "UInt16";
    private const string INT32_TOKEN = "Int32";
    private const string UINT32_TOKEN = "UInt32";
    private const string INT64_TOKEN = "Int64";
    private const string UINT64_TOKEN = "UInt64";
    private const string SINGLE_TOKEN = "Single";
    private const string DOUBLE_TOKEN = "Double";
    private const string DECIMAL_TOKEN = "Decimal";
    private const string DATETIME_TOKEN = "DateTime";
    private const string DATETIMEOFFSET_TOKEN = "DateTimeOffset";
    private const string TIMESPAN_TOKEN = "TimeSpan";
    private const string GUID_TOKEN = "Guid";
    private const string STRING_TOKEN = "String";

    private static void WriteTypedValue(
        Utf8JsonWriter writer,
        object value,
        JsonSerializerOptions options)
    {
        var valueType = value.GetType();
        writer.WriteStartObject();
        writer.WriteString("type", TokenFor(valueType));
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, value, valueType, options);
        writer.WriteEndObject();
    }

    private static string TokenFor(Type type)
    {
        return Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => BOOLEAN_TOKEN,
            TypeCode.Char => CHAR_TOKEN,
            TypeCode.SByte => SBYTE_TOKEN,
            TypeCode.Byte => BYTE_TOKEN,
            TypeCode.Int16 => INT16_TOKEN,
            TypeCode.UInt16 => UINT16_TOKEN,
            TypeCode.Int32 => INT32_TOKEN,
            TypeCode.UInt32 => UINT32_TOKEN,
            TypeCode.Int64 => INT64_TOKEN,
            TypeCode.UInt64 => UINT64_TOKEN,
            TypeCode.Single => SINGLE_TOKEN,
            TypeCode.Double => DOUBLE_TOKEN,
            TypeCode.Decimal => DECIMAL_TOKEN,
            TypeCode.DateTime => DATETIME_TOKEN,
            TypeCode.String => STRING_TOKEN,
            _ => TokenForStructuralType(type)
        };
    }

    private static string TokenForStructuralType(Type type)
    {
        if (type == typeof(Guid))
        {
            return GUID_TOKEN;
        }

        if (type == typeof(TimeSpan))
        {
            return TIMESPAN_TOKEN;
        }

        if (type == typeof(DateTimeOffset))
        {
            return DATETIMEOFFSET_TOKEN;
        }

        return type.FullName ?? type.Name;
    }

    private static object? MaterializeElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("type", out var typeProperty)
            && element.TryGetProperty("value", out var valueProperty)
            && typeProperty.ValueKind == JsonValueKind.String)
        {
            return ReconstructTypedValue(typeProperty.GetString(), valueProperty);
        }

        return MaterializeUntyped(element);
    }

    private static object? ReconstructTypedValue(string? token, JsonElement valueElement)
    {
        var rawValue = valueElement.GetRawText();
        return token switch
        {
            BOOLEAN_TOKEN => JsonSerializer.Deserialize<bool>(rawValue),
            CHAR_TOKEN => JsonSerializer.Deserialize<char>(rawValue),
            SBYTE_TOKEN => JsonSerializer.Deserialize<sbyte>(rawValue),
            BYTE_TOKEN => JsonSerializer.Deserialize<byte>(rawValue),
            INT16_TOKEN => JsonSerializer.Deserialize<short>(rawValue),
            UINT16_TOKEN => JsonSerializer.Deserialize<ushort>(rawValue),
            INT32_TOKEN => JsonSerializer.Deserialize<int>(rawValue),
            UINT32_TOKEN => JsonSerializer.Deserialize<uint>(rawValue),
            INT64_TOKEN => JsonSerializer.Deserialize<long>(rawValue),
            UINT64_TOKEN => JsonSerializer.Deserialize<ulong>(rawValue),
            SINGLE_TOKEN => JsonSerializer.Deserialize<float>(rawValue),
            DOUBLE_TOKEN => JsonSerializer.Deserialize<double>(rawValue),
            DECIMAL_TOKEN => JsonSerializer.Deserialize<decimal>(rawValue),
            DATETIME_TOKEN => JsonSerializer.Deserialize<DateTime>(rawValue),
            DATETIMEOFFSET_TOKEN => JsonSerializer.Deserialize<DateTimeOffset>(rawValue),
            TIMESPAN_TOKEN => JsonSerializer.Deserialize<TimeSpan>(rawValue),
            GUID_TOKEN => JsonSerializer.Deserialize<Guid>(rawValue),
            STRING_TOKEN => JsonSerializer.Deserialize<string>(rawValue),
            _ => MaterializeUntyped(valueElement)
        };
    }

    private static object? MaterializeUntyped(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            {
                return element.GetString();
            }
            case JsonValueKind.Number:
            {
                if (element.TryGetInt32(out var intValue))
                {
                    return intValue;
                }

                if (element.TryGetInt64(out var longValue))
                {
                    return longValue;
                }

                if (element.TryGetDouble(out var doubleValue))
                {
                    return doubleValue;
                }

                throw new JsonException($"Unsupported numeric value: {element.GetRawText()}");
            }
            case JsonValueKind.True:
            {
                return true;
            }
            case JsonValueKind.False:
            {
                return false;
            }
            case JsonValueKind.Null:
            {
                return null;
            }
            case JsonValueKind.Object:
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText());
            }
            case JsonValueKind.Array:
            {
                return JsonSerializer.Deserialize<List<object>>(element.GetRawText());
            }
            default:
            {
                throw new JsonException($"Unsupported token type: {element.ValueKind}");
            }
        }
    }
}
