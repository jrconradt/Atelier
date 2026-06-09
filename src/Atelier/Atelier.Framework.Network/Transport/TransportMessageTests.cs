using System.Text;
using Atelier.Framework.Attributes;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Network.Transport;

public static class TransportMessageTests
{
    private const string TARGET = "global::Atelier.Framework.Network.Transport.TransportMessage";

    private static void IsTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    [Contract("SamplePayload", Version = "1.0", Namespace = "Framework.Network.Transport")]
    private sealed class SamplePayload
    {
        public int Number { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    [GeneratedTest("transport.message.payload-roundtrips", TARGET)]
    public static void CreateThenDeserializeRoundTripsThePayload()
    {
        var message = TransportMessage.Create(
            "sample",
            new SamplePayload { Number = 42, Text = "atelier" });

        var payload = message.DeserializePayload<SamplePayload>();

        if (payload.Number != 42)
        {
            throw new InvalidOperationException($"Expected Number 42, got {payload.Number}");
        }

        if (payload.Text != "atelier")
        {
            throw new InvalidOperationException($"Expected Text 'atelier', got '{payload.Text}'");
        }
    }

    [GeneratedTest("transport.message.create-sets-type-and-id", TARGET)]
    public static void CreateSetsMessageTypeAndId()
    {
        var message = TransportMessage.Create("sample", new SamplePayload());

        if (message.MessageType != "sample")
        {
            throw new InvalidOperationException($"Expected MessageType 'sample', got '{message.MessageType}'");
        }

        if (string.IsNullOrEmpty(message.MessageId))
        {
            throw new InvalidOperationException("MessageId should be assigned");
        }
    }

    [GeneratedTest("transport.message.headers-set-and-get", TARGET)]
    public static void HeadersSetAndGet()
    {
        var message = new TransportMessage();

        if (message.HasHeaders)
        {
            throw new InvalidOperationException("New message should have no headers");
        }

        message.SetHeader("correlation", "abc-123");

        if (!message.HasHeaders)
        {
            throw new InvalidOperationException("Message should report headers after SetHeader");
        }

        if (!message.TryGetHeader("correlation", out var value) || value != "abc-123")
        {
            throw new InvalidOperationException($"Expected header 'abc-123', got '{value}'");
        }
    }

    [GeneratedTest("transport.message.empty-payload-throws", TARGET)]
    public static void DeserializeEmptyPayloadThrowsInvalidOperation()
    {
        var message = new TransportMessage();

        var threw = false;
        try
        {
            message.DeserializePayload<SamplePayload>();
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        IsTrue(threw, "Deserializing an empty payload must throw InvalidOperationException");
    }

    [GeneratedTest("transport.message.arbitrary-payload-throws-only-known", TARGET)]
    public static void DeserializeArbitraryPayloadThrowsOnlyKnownExceptions()
    {
        const int FUZZ_SEED = 0x5EED_BEEF;
        const int FUZZ_ITERATIONS = 4096;
        const int MAX_FUZZ_BYTES = 8192;

        var random = new Random(FUZZ_SEED);

        for (var iteration = 0; iteration < FUZZ_ITERATIONS; iteration++)
        {
            var bytes = GenerateFuzzPayload(random, MAX_FUZZ_BYTES);
            var message = new TransportMessage { Payload = bytes };
            try
            {
                message.DeserializePayload<SamplePayload>();
            }
            catch (System.Text.Json.JsonException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"DeserializePayload threw an unexpected {ex.GetType().Name} at iteration {iteration}");
            }
        }
    }

    private static byte[] GenerateFuzzPayload(Random random, int maxBytes)
    {
        var shape = random.Next(0, 5);

        if (shape == 0)
        {
            var length = random.Next(0, maxBytes);
            var raw = new byte[length];
            random.NextBytes(raw);
            return raw;
        }

        if (shape == 1)
        {
            var depth = random.Next(0, 512);
            var open = new string('[', depth);
            return Encoding.UTF8.GetBytes(open);
        }

        if (shape == 2)
        {
            var length = random.Next(0, maxBytes);
            var fragment = "{\"Name\":\"" + new string('x', length) + "\"";
            return Encoding.UTF8.GetBytes(fragment);
        }

        if (shape == 3)
        {
            var json = "{\"Name\":" + random.Next(int.MinValue, int.MaxValue) + ",\"Value\":\"" + random.Next() + "\"}";
            var raw = Encoding.UTF8.GetBytes(json);
            var truncateAt = random.Next(0, raw.Length + 1);
            return raw.AsSpan(0, truncateAt).ToArray();
        }

        var size = random.Next(0, maxBytes);
        var ascii = new byte[size];
        for (var i = 0; i < size; i++)
        {
            ascii[i] = (byte)random.Next(32, 127);
        }

        return ascii;
    }
}
