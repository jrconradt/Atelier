using Atelier.Framework.Context;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Messaging.Serializers;

public static class MessagingContextSerializerTests
{
    private const string TARGET = "global::Atelier.Framework.Messaging.Serializers.MessagingContextSerializer";

    [GeneratedTest("messaging.context.results-roundtrip", TARGET)]
    public static void ResultsRoundTripThroughSerialization()
    {
        var serializer = new MessagingContextSerializer();

        var context = new CompositeContext(
            "ctx-roundtrip",
            "RoundTrip");
        context.AddResult("status", "ok");
        context.AddResult("count", 7L);
        context.AddResult("enabled", true);

        var serialized = serializer.Serialize(context);

        if (string.IsNullOrEmpty(serialized))
        {
            throw new InvalidOperationException("Serialized context should not be empty");
        }

        var restored = serializer.Deserialize(serialized);

        var status = restored.GetOutcome<string>("status");
        if (status != "ok")
        {
            throw new InvalidOperationException($"Expected status 'ok', got '{status}'");
        }

        var count = restored.GetOutcome<long>("count");
        if (count != 7L)
        {
            throw new InvalidOperationException($"Expected count 7, got {count}");
        }

        var enabled = restored.GetOutcome<bool>("enabled");
        if (!enabled)
        {
            throw new InvalidOperationException("Expected enabled true after round-trip");
        }
    }

    [GeneratedTest("messaging.context.arbitrary-input-never-throws-unexpectedly", TARGET)]
    public static void ArbitraryInputDeserializesSafely()
    {
        const int FUZZ_SEED = 0x4D_55_67_00;
        const int FUZZ_ITERATIONS = 4096;

        var serializer = new MessagingContextSerializer();
        var random = new Random(FUZZ_SEED);

        for (var iteration = 0; iteration < FUZZ_ITERATIONS; iteration++)
        {
            var input = GenerateFuzzPayload(random);

            bool tryResult;
            try
            {
                tryResult = serializer.TryDeserialize(input, out _);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"TryDeserialize threw on input '{input}' at iteration {iteration}: {ex.GetType().Name}");
            }

            try
            {
                serializer.Deserialize(input);
                if (!tryResult
                    && !string.IsNullOrWhiteSpace(input))
                {
                    throw new InvalidOperationException(
                        $"Deserialize accepted input that TryDeserialize rejected at iteration {iteration}: '{input}'");
                }
            }
            catch (InvalidOperationException) when (!tryResult)
            {
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Deserialize threw an unexpected {ex.GetType().Name} on input '{input}' at iteration {iteration}");
            }
        }
    }

    private static string GenerateFuzzPayload(Random random)
    {
        var shape = random.Next(0, 6);

        if (shape == 0)
        {
            var length = random.Next(0, 256);
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = (char)random.Next(0, 128);
            }

            return new string(chars);
        }

        if (shape == 1)
        {
            var length = random.Next(0, 8192);
            var raw = new byte[length];
            random.NextBytes(raw);
            return Convert.ToBase64String(raw);
        }

        if (shape == 2)
        {
            var depth = random.Next(0, 1024);
            return new string('[', depth);
        }

        if (shape == 3)
        {
            var size = random.Next(0, 8192);
            return "{\"data\":{\"a\":\"" + new string('x', size) + "\"}}";
        }

        if (shape == 4)
        {
            var truncated = "{\"version\":1,\"contextId\":\"c\",\"results\":{\"r\":\"" + new string('y', random.Next(0, 512)) + "\"}";
            var cut = random.Next(0, truncated.Length + 1);
            return truncated[..cut];
        }

        return "{\"version\":" + random.Next(0, int.MaxValue) + ",\"data\":null,\"results\":null}";
    }
}
