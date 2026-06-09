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
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt32(out var i) => i,
            JsonTokenType.Number when reader.TryGetInt64(out var l) => l,
            JsonTokenType.Number when reader.TryGetDouble(out var d) => d,
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Null => null,
            JsonTokenType.StartObject => JsonSerializer.Deserialize<Dictionary<string, object>>(ref reader),
            JsonTokenType.StartArray => JsonSerializer.Deserialize<List<object>>(ref reader),
            _ => throw new JsonException($"Unsupported token type: {reader.TokenType}")
        };
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
            JsonSerializer.Serialize(writer, kvp.Value, kvp.Value?.GetType() ?? typeof(object), options);
        }

        writer.WriteEndObject();
    }
}
