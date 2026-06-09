using Atelier.Framework.Context;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Network;

public static class WireContextCodecTests
{
    private const string TARGET = "global::Atelier.Framework.Network.WireContextCodec";

    private static void IsTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static IContext BuildContext()
    {
        var authorization = AuthorizationContext.Create("u1", "t1", "s1");
        authorization.AddClaim("dept", "eng");
        authorization.AddRole("admin");

        var context = new CompositeContext(
            Guid.NewGuid().ToString(),
            "test",
            null,
            new Dictionary<string, string>());

        context.TraceId = "trace-1";
        context.CorrelationId = "corr-1";
        context.SpanId = "span-1";
        context.Authorization = authorization;

        return context;
    }

    [GeneratedTest("network.wirecontext.roundtrips-identity-and-claims", TARGET)]
    public static void RoundTripsIdentityAndClaims()
    {
        var encoded = WireContextCodec.Encode(BuildContext());
        IsTrue(!string.IsNullOrEmpty(encoded), "Encoding a populated context should produce a header value");

        var decoded = WireContextCodec.Decode(encoded);
        IsTrue(decoded != null, "Decoding a valid header should produce a context");
        IsTrue(decoded!.TraceId == "trace-1", $"TraceId should round-trip, got {decoded.TraceId}");
        IsTrue(decoded.CorrelationId == "corr-1", $"CorrelationId should round-trip, got {decoded.CorrelationId}");
        IsTrue(decoded.Authorization?.UserId == "u1", $"UserId should round-trip, got {decoded.Authorization?.UserId}");
        IsTrue(decoded.Authorization!.HasClaim("dept"), "Claim 'dept' should round-trip");
        IsTrue(decoded.Authorization.HasRole("admin"), "Role 'admin' should round-trip");
    }

    [GeneratedTest("network.wirecontext.decoded-identity-is-unverified", TARGET)]
    public static void DecodedIdentityIsUnverified()
    {
        var decoded = WireContextCodec.Decode(WireContextCodec.Encode(BuildContext()));

        IsTrue(decoded?.Authorization != null, "Decoded context should carry authorization");
        IsTrue(!decoded!.Authorization!.IsVerified, "Wire-sourced identity must be unverified");
        IsTrue(!decoded.Authorization.IsValid(), "Unverified wire identity must not be valid for authorization");
    }

    [GeneratedTest("network.wirecontext.malformed-header-decodes-to-null", TARGET)]
    public static void MalformedHeaderDecodesToNull()
    {
        IsTrue(WireContextCodec.Decode("not%%%base64") == null, "A malformed header must decode to null");
        IsTrue(WireContextCodec.Decode(string.Empty) == null, "An empty header must decode to null");
    }

    [GeneratedTest("network.wirecontext.arbitrary-input-never-throws", TARGET)]
    public static void ArbitraryInputDecodesToNullOrContextWithoutThrowing()
    {
        const int FUZZ_SEED = 0x57A1_C0DE;
        const int FUZZ_ITERATIONS = 4096;

        var random = new Random(FUZZ_SEED);

        for (var iteration = 0; iteration < FUZZ_ITERATIONS; iteration++)
        {
            var input = GenerateFuzzHeader(random);
            try
            {
                var decoded = WireContextCodec.Decode(input);
                IsTrue(decoded == null || decoded.Authorization != null, "A decoded context must carry authorization");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Decode threw on input '{input}' at iteration {iteration}: {ex.GetType().Name}");
            }
        }
    }

    private static string? GenerateFuzzHeader(Random random)
    {
        var shape = random.Next(0, 6);

        if (shape == 0)
        {
            return null;
        }

        if (shape == 1)
        {
            var length = random.Next(0, 256);
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = (char)random.Next(0, 128);
            }

            return new string(chars);
        }

        if (shape == 2)
        {
            var length = random.Next(0, 8192);
            var raw = new byte[length];
            random.NextBytes(raw);
            return Convert.ToBase64String(raw);
        }

        if (shape == 3)
        {
            var depth = random.Next(0, 1024);
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(new string('[', depth)));
        }

        if (shape == 4)
        {
            var size = random.Next(0, 8192);
            var fragment = "{\"Claims\":{\"a\":\"" + new string('x', size) + "\"}}";
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(fragment));
        }

        var truncated = "{\"UserId\":\"user\",\"Roles\":{\"r\":\"" + new string('y', random.Next(0, 512)) + "\"}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(truncated);
        var cut = random.Next(0, bytes.Length + 1);
        return Convert.ToBase64String(bytes.AsSpan(0, cut).ToArray());
    }

    [GeneratedTest("network.wirecontext.oversized-payload-decodes-to-null", TARGET)]
    public static void OversizedPayloadDecodesToNull()
    {
        var oversized = new string('A', (64 * 1024) + 1024);
        var header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{{\"UserId\":\"{oversized}\"}}"));

        IsTrue(WireContextCodec.Decode(header) == null, "An over-budget payload must decode to null before deserialization");
    }

    [GeneratedTest("network.wirecontext.authorization-entries-are-capped", TARGET)]
    public static void AuthorizationEntriesAreCapped()
    {
        var authorization = AuthorizationContext.Create("u1");
        for (var i = 0; i < 256; i++)
        {
            authorization.AddRole($"role-{i}");
        }

        var context = new CompositeContext(
            Guid.NewGuid().ToString(),
            "test",
            null,
            new Dictionary<string, string>());
        context.Authorization = authorization;

        var decoded = WireContextCodec.Decode(WireContextCodec.Encode(context));

        IsTrue(decoded?.Authorization != null, "Decoded context should carry authorization");
        IsTrue(decoded!.Authorization!.Roles.Count <= 64, $"Roles must be capped at 64, got {decoded.Authorization.Roles.Count}");
    }
}
