using System.Text.Json;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Properties;

public static class TypedPropertyBagJsonConverterTests
{
    private const string TARGET = "global::Atelier.Framework.Properties.TypedPropertyBagJsonConverter";

    private static readonly JsonSerializerOptions Options = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new TypedPropertyBagJsonConverter());
        return options;
    }

    private static void IsTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    [GeneratedTest("properties.typedpropertybag.roundtrips-scalar-values", TARGET)]
    public static void RoundTripsScalarValues()
    {
        var bag = new TypedPropertyBag();
        bag.Set("name", "atelier");
        bag.Set("count", 7);
        bag.Set("big", 9_000_000_000L);
        bag.Set("ratio", 1.5d);
        bag.Set("enabled", true);

        var json = JsonSerializer.Serialize(bag, Options);
        var restored = JsonSerializer.Deserialize<TypedPropertyBag>(json, Options);

        IsTrue(restored != null, "Deserialization should produce a bag");
        IsTrue(restored!.GetOrDefault<string>("name", string.Empty) == "atelier", "String value should round-trip");
        IsTrue(restored.GetOrDefault<int>("count", 0) == 7, "Int value should round-trip");
        IsTrue(restored.GetOrDefault<long>("big", 0L) == 9_000_000_000L, "Long value should round-trip");
        IsTrue(restored.GetOrDefault<double>("ratio", 0d) == 1.5d, "Double value should round-trip");
        IsTrue(restored.GetOrDefault<bool>("enabled", false), "Bool value should round-trip");
    }

    [GeneratedTest("properties.typedpropertybag.arbitrary-json-throws-only-jsonexception", TARGET)]
    public static void ArbitraryJsonThrowsOnlyJsonException()
    {
        var random = new Random(0x5eed);

        for (var iteration = 0; iteration < 2000; iteration++)
        {
            var input = GenerateArbitraryJson(random);

            try
            {
                JsonSerializer.Deserialize<TypedPropertyBag>(input, Options);
            }
            catch (JsonException)
            {
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Deserialize threw an unexpected {ex.GetType().Name} on input '{input}'");
            }
        }
    }

    private static string GenerateArbitraryJson(Random random)
    {
        var maxDepth = random.Next(1, 7);
        var parts = new List<string>();
        var stack = new Stack<JsonFrame>();
        stack.Push(new JsonFrame(0));

        while (stack.Count > 0)
        {
            var frame = stack.Pop();

            if (frame.Depth >= maxDepth
                || random.Next(3) == 0)
            {
                parts.Add(GenerateScalar(random));
                continue;
            }

            if (random.Next(2) == 0)
            {
                var memberCount = random.Next(0, 4);
                parts.Add("{");
                for (var i = 0; i < memberCount; i++)
                {
                    if (i > 0)
                    {
                        parts.Add(",");
                    }

                    parts.Add(GenerateKey(random));
                    parts.Add(random.Next(8) == 0 ? "" : ":");
                    stack.Push(new JsonFrame(frame.Depth + 1));
                }

                parts.Add(random.Next(8) == 0 ? "" : "}");
            }
            else
            {
                var elementCount = random.Next(0, 4);
                parts.Add("[");
                for (var i = 0; i < elementCount; i++)
                {
                    if (i > 0)
                    {
                        parts.Add(",");
                    }

                    stack.Push(new JsonFrame(frame.Depth + 1));
                }

                parts.Add(random.Next(8) == 0 ? "" : "]");
            }
        }

        if (random.Next(5) == 0)
        {
            return CorruptJson(string.Concat(parts), random);
        }

        return string.Concat(parts);
    }

    private static string GenerateScalar(Random random)
    {
        return random.Next(9) switch
        {
            0 => GenerateString(random),
            1 => $"{random.Next(int.MinValue, int.MaxValue)}",
            2 => $"{(long)random.Next() * random.Next()}",
            3 => $"{random.NextDouble() * 1e300:R}",
            4 => "true",
            5 => "false",
            6 => "null",
            7 => "9999999999999999999999999999",
            _ => "1e9999"
        };
    }

    private static string GenerateKey(Random random)
    {
        if (random.Next(6) == 0)
        {
            return GenerateScalar(random);
        }

        return GenerateString(random);
    }

    private static string GenerateString(Random random)
    {
        var length = random.Next(0, 8);
        var fragments = new List<string>();
        for (var i = 0; i < length; i++)
        {
            fragments.Add(random.Next(6) switch
            {
                0 => "\\u0001",
                1 => "\\u001f",
                2 => $"\\u{random.Next(0xD800, 0xDC00):X4}",
                3 => "\\\"",
                4 => "\\\\",
                _ => $"{(char)random.Next('a', 'z' + 1)}"
            });
        }

        var body = string.Concat(fragments);
        return $"\"{body}\"";
    }

    private static string CorruptJson(string json, Random random)
    {
        if (json.Length == 0)
        {
            return "{";
        }

        var index = random.Next(json.Length);
        return random.Next(3) switch
        {
            0 => json.Remove(index, 1),
            1 => json.Insert(index, "}"),
            _ => json.Insert(index, "@")
        };
    }

    private readonly struct JsonFrame
    {
        public JsonFrame(int depth)
        {
            Depth = depth;
        }

        public int Depth { get; }
    }

    [GeneratedTest("properties.typedpropertybag.empty-object-roundtrips", TARGET)]
    public static void EmptyObjectRoundTrips()
    {
        var restored = JsonSerializer.Deserialize<TypedPropertyBag>("{}", Options);

        IsTrue(restored != null, "An empty object should deserialize to an empty bag");
        IsTrue(restored!.Count == 0, $"An empty object should produce zero entries, got {restored.Count}");
    }
}
