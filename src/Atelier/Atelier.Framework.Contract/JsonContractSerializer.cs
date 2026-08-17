using Atelier.Framework.Primitives;
using System.Buffers;
using System.Text.Json;
using Atelier.Framework.Attributes;
using Atelier.Framework.Context.Validation;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Properties;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Contract;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class JsonContractSerializer : IContractSerializer, IAtelier
{
        [Requisite] protected readonly IContractRegistry _registry = null!;

        [Requisite] protected readonly IContractMigrator _migrator = null!;

        [Requisite] protected readonly IContractValidator _validator = null!;

        private readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            MaxDepth = 32,
            Converters = { new TypedPropertyBagJsonConverter() }
        };

        public byte[] Serialize<T>(T contract) where T : class
    {
        ArgumentNullException.ThrowIfNull(contract);
        return JsonSerializer.SerializeToUtf8Bytes(
            contract,
            _options);
    }

        public byte[] Serialize(
        object contract,
        Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(contractType);
        return JsonSerializer.SerializeToUtf8Bytes(
            contract,
            contractType,
            _options);
    }

        public T? Deserialize<T>(byte[] data) where T : class
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > ContextSizeValidator.DEFAULT_MAX_CONTEXT_SIZE_BYTES)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Payload exceeds maximum size"), ("PayloadBytes", data.Length), ("MaxBytes", ContextSizeValidator.DEFAULT_MAX_CONTEXT_SIZE_BYTES)]);
            return null;
        }
        return JsonSerializer.Deserialize<T>(
            data,
            _options);
    }

        public object? Deserialize(
        byte[] data,
        Type contractType)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(contractType);
        if (data.Length > ContextSizeValidator.DEFAULT_MAX_CONTEXT_SIZE_BYTES)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Payload exceeds maximum size"), ("PayloadBytes", data.Length), ("MaxBytes", ContextSizeValidator.DEFAULT_MAX_CONTEXT_SIZE_BYTES)]);
            return null;
        }
        return JsonSerializer.Deserialize(
            data,
            contractType,
            _options);
    }

        public Outcome<SerializedContract> SerializeWithMetadata<T>(T contract) where T : class
    {
        ArgumentNullException.ThrowIfNull(contract);

        var metadata = _registry.Resolve<T>();
        if (metadata == null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Contract is not registered"), ("ContractType", typeof(T).FullName ?? typeof(T).Name)]);
            return Outcome<SerializedContract>.Failure();
        }

        var payload = Serialize(contract);

        return Outcome<SerializedContract>.Success(new SerializedContract
        {
            ContractName = metadata.Name,
            ContractVersion = metadata.Version,
            ContractNamespace = metadata.Namespace,
            Payload = payload,
            SerializationFormat = "application/json"
        });
    }

        [Operation("DeserializeWithMetadata")]
    public Outcome<object?> DeserializeWithMetadata(SerializedContract serialized)
    {
        if (serialized is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "serialized is required")]);
            return Outcome<object?>.Failure();
        }

        var metadata = _registry.Resolve(
            serialized.ContractName,
            serialized.ContractVersion,
            serialized.ContractNamespace);

        if (metadata == null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Contract is not registered"), ("Contract", serialized.ContractName), ("Version", serialized.ContractVersion)]);
            return Outcome<object?>.Failure();
        }

        return DeserializeWithMetadata(
            serialized,
            metadata.ContractType);
    }

        [Operation("DeserializeWithMetadata")]
    public Outcome<object?> DeserializeWithMetadata(
        SerializedContract serialized,
        Type? expectedType)
    {
        if (serialized is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "serialized is required")]);
            return Outcome<object?>.Failure();
        }


        if (!IsJsonFormat(serialized.SerializationFormat))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Unsupported serialization format"), ("Format", serialized.SerializationFormat ?? string.Empty)]);
            return Outcome<object?>.Failure();
        }

        var metadata = _registry.Resolve(
            serialized.ContractName,
            serialized.ContractVersion,
            serialized.ContractNamespace);

        if (metadata == null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Contract is not registered"), ("Contract", serialized.ContractName), ("Version", serialized.ContractVersion)]);
            return Outcome<object?>.Failure();
        }

        if (expectedType == null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "An expected contract type is required for metadata-driven deserialization"), ("Contract", serialized.ContractName)]);
            return Outcome<object?>.Failure();
        }

        if (metadata.ContractType != expectedType)
        {
            var expectedMetadata = _registry.Resolve(expectedType);
            if (expectedMetadata == null)
            {
                Observe(
                    LogLevel.Warning,
                    null,
                    values: [("Reason", "Expected contract type is not registered"), ("ExpectedType", expectedType.FullName ?? expectedType.Name)]);
                return Outcome<object?>.Failure();
            }

            if (!_migrator.CanMigrate(
                metadata.Name,
                metadata.Version,
                expectedMetadata.Version))
            {
                Observe(
                    LogLevel.Warning,
                    null,
                    values: [("Reason", "Resolved contract type does not match the expected type and no migration path exists"), ("Contract", metadata.Name), ("StoredVersion", metadata.Version), ("ExpectedVersion", expectedMetadata.Version)]);
                return Outcome<object?>.Failure();
            }

            if (!_migrator.IsBackwardCompatiblePath(
                metadata.Name,
                metadata.Version,
                expectedMetadata.Version))
            {
                Observe(
                    LogLevel.Warning,
                    null,
                    values: [("Reason", "Migration path between the stored and expected contract versions traverses a breaking schema change"), ("Contract", metadata.Name), ("StoredVersion", metadata.Version), ("ExpectedVersion", expectedMetadata.Version)]);
                return Outcome<object?>.Failure();
            }

            var decoded = Deserialize(
                serialized.Payload,
                metadata.ContractType);

            if (decoded == null)
            {
                Observe(
                    LogLevel.Warning,
                    null,
                    values: [("Reason", "Failed to deserialize the stored contract payload"), ("Contract", metadata.Name), ("Version", metadata.Version)]);
                return Outcome<object?>.Failure();
            }

            var migrated = _migrator.Migrate(
                decoded,
                metadata.ContractType,
                expectedType,
                expectedMetadata.Version);

            if (!migrated.IsSuccess)
            {
                return migrated;
            }

            return ValidateAgainstExpected(
                migrated.Data,
                expectedType);
        }

        var deserialized = Deserialize(
            serialized.Payload,
            expectedType);

        return ValidateAgainstExpected(
            deserialized,
            expectedType);
    }

        private Outcome<object?> ValidateAgainstExpected(
        object? contract,
        Type expectedType)
    {
        if (contract == null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Failed to deserialize the stored contract payload"), ("ExpectedType", expectedType.FullName ?? expectedType.Name)]);
            return Outcome<object?>.Failure();
        }

        var validation = _validator.Validate(
            contract,
            expectedType);

        if (!validation.IsValid)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Contract validation failed"), ("ExpectedType", expectedType.FullName ?? expectedType.Name), ("Detail", validation.ErrorMessage ?? string.Empty)]);
            return Outcome<object?>.Failure();
        }

        return Outcome<object?>.Success(contract);
    }

        private static bool IsJsonFormat(string? format)
    {
        return string.Equals(format, "application/json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
    }

        [Operation("SerializeToBuffer")]
    public Outcome<ReadOnlyMemory<byte>> SerializeToBuffer<T>(T contract) where T : class
    {

        if (contract is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "contract cannot be null"), ("ContractType", typeof(T).FullName ?? typeof(T).Name)]);
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }

        try
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(bufferWriter);
            JsonSerializer.Serialize(writer, contract, _options);
            writer.Flush();

            var result = bufferWriter.WrittenMemory.ToArray();
            return Outcome<ReadOnlyMemory<byte>>.Success(result);
        }
        catch (JsonException ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Reason", "Serialization failed"), ("ContractType", typeof(T).FullName ?? typeof(T).Name)]);
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }
        catch (NotSupportedException ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Reason", "Serialization is not supported for the contract type"), ("ContractType", typeof(T).FullName ?? typeof(T).Name)]);
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }
    }

        [Operation("SerializeToBuffer")]
    public Outcome<ReadOnlyMemory<byte>> SerializeToBuffer(
        object contract,
        Type contractType)
    {
        if (contract is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "contract cannot be null")]);
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }
        if (contractType is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "contractType cannot be null")]);
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }


        try
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(bufferWriter);
            JsonSerializer.Serialize(writer, contract, contractType, _options);
            writer.Flush();

            var result = bufferWriter.WrittenMemory.ToArray();
            return Outcome<ReadOnlyMemory<byte>>.Success(result);
        }
        catch (JsonException ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Reason", "Serialization failed"), ("ContractType", contractType.FullName ?? contractType.Name)]);
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }
        catch (NotSupportedException ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Reason", "Serialization is not supported for the contract type"), ("ContractType", contractType.FullName ?? contractType.Name)]);
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }
    }

        [Operation("SerializeToBufferAsync")]
    public async Task<Outcome<ReadOnlyMemory<byte>>> SerializeToBufferAsync<T>(
        T contract,
        CancellationToken cancellationToken = default) where T : class
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }


        if (contract is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "contract cannot be null"), ("ContractType", typeof(T).FullName ?? typeof(T).Name)]);
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }

        try
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(bufferWriter);
            JsonSerializer.Serialize(writer, contract, _options);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            var result = bufferWriter.WrittenMemory.ToArray();
            return Outcome<ReadOnlyMemory<byte>>.Success(result);
        }
        catch (JsonException ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Reason", "Serialization failed"), ("ContractType", typeof(T).FullName ?? typeof(T).Name)]);
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }
        catch (NotSupportedException ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Reason", "Serialization is not supported for the contract type"), ("ContractType", typeof(T).FullName ?? typeof(T).Name)]);
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }
    }

        [Operation("SerializeToBufferAsync")]
    public async Task<Outcome<ReadOnlyMemory<byte>>> SerializeToBufferAsync(
        object contract,
        Type contractType,
        CancellationToken cancellationToken = default)
    {
        if (contract is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "contract cannot be null")]);
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }
        if (contractType is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "contractType cannot be null")]);
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }


        try
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(bufferWriter);
            JsonSerializer.Serialize(writer, contract, contractType, _options);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            var result = bufferWriter.WrittenMemory.ToArray();
            return Outcome<ReadOnlyMemory<byte>>.Success(result);
        }
        catch (JsonException ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Reason", "Serialization failed"), ("ContractType", contractType.FullName ?? contractType.Name)]);
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }
        catch (NotSupportedException ex)
        {
            Observe(
                LogLevel.Error,
                ex,
                values: [("Reason", "Serialization is not supported for the contract type"), ("ContractType", contractType.FullName ?? contractType.Name)]);
            return Outcome<ReadOnlyMemory<byte>>.Failure();
        }
    }
}
