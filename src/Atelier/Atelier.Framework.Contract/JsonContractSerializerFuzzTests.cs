using System.Text;
using System.Text.Json;
using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Contract;

public static class JsonContractSerializerFuzzTests
{
    private const string TARGET = "global::Atelier.Framework.Contract.JsonContractSerializer";

    [Contract("FuzzContract", Version = "1.0", Namespace = "Framework.Contract.Fuzz")]
    private sealed class FuzzContract
    {
        public required string Name { get; set; }

        public int Value { get; set; }
    }

    private static JsonContractSerializer BuildSerializer()
    {
        var registry = new ContractRegistry();
        registry.Register<FuzzContract>();

        var migrator = new ContractMigrator(registry,
                                            null);
        var validator = new ContractValidator(registry);

        return new JsonContractSerializer(
            registry,
            migrator,
            validator,
            null);
    }

    private static byte[] SeedPayload(int seed)
    {
        var bytes = new byte[seed % 257];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)((seed * 31 + index * 7) & 0xFF);
        }

        return bytes;
    }

    [GeneratedTest("contract.serializer.malformed-bytes-never-crash", TARGET)]
    public static void DeserializeOfMalformedBytesNeverCrashes()
    {
        var serializer = BuildSerializer();

        var corpus = new List<byte[]>
        {
            Array.Empty<byte>(),
            new byte[] { 0x7B },
            new byte[] { 0x7B, 0x22 },
            Encoding.UTF8.GetBytes("{\"Name\":"),
            Encoding.UTF8.GetBytes("not json at all"),
            Encoding.UTF8.GetBytes("[1, 2, 3"),
            Encoding.UTF8.GetBytes("{\"Value\":\"not-an-int\"}"),
            new byte[] { 0xFF, 0xFE, 0xFD, 0xFC }
        };

        for (var seed = 0; seed < 64; seed++)
        {
            corpus.Add(SeedPayload(seed));
        }

        foreach (var payload in corpus)
        {
            try
            {
                serializer.Deserialize<FuzzContract>(payload);
            }
            catch (JsonException)
            {
            }

            try
            {
                serializer.Deserialize(
                    payload,
                    typeof(FuzzContract));
            }
            catch (JsonException)
            {
            }
        }
    }

    [GeneratedTest("contract.serializer.malformed-metadata-returns-failure", TARGET)]
    public static void DeserializeWithMetadataOfMalformedPayloadReturnsFailure()
    {
        var serializer = BuildSerializer();

        for (var seed = 0; seed < 128; seed++)
        {
            var envelope = new SerializedContract
            {
                ContractName = "FuzzContract",
                ContractVersion = "1.0",
                ContractNamespace = "Framework.Contract.Fuzz",
                Payload = SeedPayload(seed),
                SerializationFormat = "application/json"
            };

            Outcome<object?> outcome;
            try
            {
                outcome = serializer.DeserializeWithMetadata(
                    envelope,
                    typeof(FuzzContract));
            }
            catch (JsonException)
            {
                continue;
            }

            if (outcome.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Malformed payload (seed {seed}) deserialized as success");
            }
        }
    }

    [GeneratedTest("contract.serializer.unknown-contract-returns-failure", TARGET)]
    public static void DeserializeWithMetadataOfUnknownContractReturnsFailure()
    {
        var serializer = BuildSerializer();

        var envelope = new SerializedContract
        {
            ContractName = "NeverRegistered",
            ContractVersion = "9.9",
            ContractNamespace = "Framework.Contract.Fuzz",
            Payload = Encoding.UTF8.GetBytes("{\"Name\":\"ok\",\"Value\":1}"),
            SerializationFormat = "application/json"
        };

        var outcome = serializer.DeserializeWithMetadata(
            envelope,
            typeof(FuzzContract));

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Unknown stored contract should fail");
        }
    }

    [GeneratedTest("contract.serializer.unsupported-format-returns-failure", TARGET)]
    public static void DeserializeWithMetadataOfUnsupportedFormatReturnsFailure()
    {
        var serializer = BuildSerializer();

        var envelope = new SerializedContract
        {
            ContractName = "FuzzContract",
            ContractVersion = "1.0",
            ContractNamespace = "Framework.Contract.Fuzz",
            Payload = Encoding.UTF8.GetBytes("{\"Name\":\"ok\",\"Value\":1}"),
            SerializationFormat = "application/x-msgpack"
        };

        var outcome = serializer.DeserializeWithMetadata(
            envelope,
            typeof(FuzzContract));

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Unsupported serialization format should fail");
        }
    }

    [GeneratedTest("contract.serializer.resolves-type-from-metadata", TARGET)]
    public static void DeserializeWithMetadataResolvesTypeFromRegistry()
    {
        var serializer = BuildSerializer();

        var envelope = new SerializedContract
        {
            ContractName = "FuzzContract",
            ContractVersion = "1.0",
            ContractNamespace = "Framework.Contract.Fuzz",
            Payload = Encoding.UTF8.GetBytes("{\"Name\":\"ok\",\"Value\":1}"),
            SerializationFormat = "application/json"
        };

        var outcome = serializer.DeserializeWithMetadata(envelope);

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException("Metadata deserialization should resolve the contract type from the registry and succeed");
        }

        if (outcome.Data is not FuzzContract decoded)
        {
            throw new InvalidOperationException("Metadata deserialization should yield the registry-resolved contract type");
        }

        if (decoded.Name != "ok" || decoded.Value != 1)
        {
            throw new InvalidOperationException("Metadata deserialization should round-trip the payload fields");
        }
    }

    [GeneratedTest("contract.serializer.unknown-contract-without-type-returns-failure", TARGET)]
    public static void DeserializeWithMetadataOfUnknownContractWithoutTypeReturnsFailure()
    {
        var serializer = BuildSerializer();

        var envelope = new SerializedContract
        {
            ContractName = "Unregistered",
            ContractVersion = "9.9",
            ContractNamespace = "Framework.Contract.Fuzz",
            Payload = Encoding.UTF8.GetBytes("{\"Name\":\"ok\",\"Value\":1}"),
            SerializationFormat = "application/json"
        };

        var outcome = serializer.DeserializeWithMetadata(envelope);

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Metadata deserialization of an unregistered contract should fail");
        }
    }
}
