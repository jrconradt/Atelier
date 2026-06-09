using System.Text.Json;
using System.Text.Json.Serialization;
using Atelier.Framework.Context.Validation;
using Atelier.Framework.Outcomes;
namespace Atelier.Framework.Messaging.Serializers
{
    public class MessageEnvelopeSerializer
    {
        private const int WIRE_MAX_DEPTH = 32;

        private readonly JsonSerializerOptions _jsonOptions;
        private readonly JsonSerializerOptions _payloadOptions;

        public MessageEnvelopeSerializer()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false,
                MaxDepth = WIRE_MAX_DEPTH,
                Converters =
                {
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
                }
            };
            _payloadOptions = new JsonSerializerOptions
            {
                MaxDepth = WIRE_MAX_DEPTH
            };
        }

                public Outcome<string> Serialize<TPayload>(MessageEnvelope<TPayload> envelope)
        {
            ArgumentNullException.ThrowIfNull(envelope);

            var serializableEnvelope = new SerializableEnvelope
            {
                MessageId = envelope.MessageId,
                Headers = envelope.Headers,
                Routing = envelope.Routing,
                PayloadJson = envelope.Payload
                    != null
                    ? JsonSerializer.Serialize(envelope.Payload, _payloadOptions)
                    : null,
            };

            var json = JsonSerializer.Serialize(serializableEnvelope, _jsonOptions);

            var serializedSizeBytes = System.Text.Encoding.UTF8.GetByteCount(json);
            if (serializedSizeBytes > ContextSizeValidator.DEFAULT_MAX_CONTEXT_SIZE_BYTES)
            {
                return Outcome<string>.Failure();
            }

            return Outcome<string>.Success(json);
        }

                public Outcome<MessageEnvelope<TPayload>> Deserialize<TPayload>(string serialized)
        {
            ArgumentNullException.ThrowIfNull(serialized);

            var serializedSizeBytes = System.Text.Encoding.UTF8.GetByteCount(serialized);
            if (serializedSizeBytes > ContextSizeValidator.DEFAULT_MAX_CONTEXT_SIZE_BYTES)
            {
                return Outcome<MessageEnvelope<TPayload>>.Failure();
            }

            SerializableEnvelope? serializableEnvelope;
            try
            {
                serializableEnvelope = JsonSerializer.Deserialize<SerializableEnvelope>(serialized, _jsonOptions);
            }
            catch (JsonException)
            {
                return Outcome<MessageEnvelope<TPayload>>.Failure();
            }

            if (serializableEnvelope == null)
            {
                return Outcome<MessageEnvelope<TPayload>>.Failure();
            }

            var envelope = new MessageEnvelope<TPayload>
            {
                MessageId = serializableEnvelope.MessageId,
                Headers = serializableEnvelope.Headers,
                Routing = serializableEnvelope.Routing
            };

            if (!string.IsNullOrEmpty(serializableEnvelope.PayloadJson))
            {
                try
                {
                    envelope.Payload = JsonSerializer.Deserialize<TPayload>(serializableEnvelope.PayloadJson, _payloadOptions);
                }
                catch (JsonException)
                {
                    return Outcome<MessageEnvelope<TPayload>>.Failure();
                }
            }

            return Outcome<MessageEnvelope<TPayload>>.Success(envelope);
        }

                public string SerializeHeadersOnly<TPayload>(MessageEnvelope<TPayload> envelope)
        {
            ArgumentNullException.ThrowIfNull(envelope);

            var headersOnly = new SerializableEnvelope
            {
                MessageId = envelope.MessageId,
                Headers = envelope.Headers,
                Routing = envelope.Routing,
                PayloadJson = null
            };

            return JsonSerializer.Serialize(headersOnly, _jsonOptions);
        }

                public Outcome<MessageEnvelope<TPayload>> DeserializeHeadersOnly<TPayload>(string serialized)
        {
            var deserialized = Deserialize<TPayload>(serialized);
            if (!deserialized.IsSuccess)
            {
                return deserialized;
            }

            var envelope = deserialized.Data;
            envelope.Payload = default(TPayload);
            return Outcome<MessageEnvelope<TPayload>>.Success(envelope);
        }

                public Outcome<MessageHeaders> ExtractHeaders<TPayload>(string serializedEnvelope)
        {
            var deserialized = Deserialize<TPayload>(serializedEnvelope);
            if (!deserialized.IsSuccess)
            {
                return Outcome<MessageHeaders>.Failure();
            }

            return Outcome<MessageHeaders>.Success(deserialized.Data.Headers);
        }

                public EnvelopeValidationResult Validate<TPayload>(MessageEnvelope<TPayload> envelope)
        {
            ArgumentNullException.ThrowIfNull(envelope);

            var validationResult = new EnvelopeValidationResult
            {
                IsValid = true
            };

            if (string.IsNullOrEmpty(envelope.MessageId))
            {
                validationResult.IsValid = false;
                validationResult.Violations.Add("MessageId is required");
            }

            if (envelope.Headers == null)
            {
                validationResult.IsValid = false;
                validationResult.Violations.Add("Headers are required");
            }
            else
            {
                if (string.IsNullOrEmpty(envelope.Headers.ContextId))
                {
                    validationResult.IsValid = false;
                    validationResult.Violations.Add("ContextId in headers is required");
                }
            }

            if (envelope.Payload != null)
            {
                var serializableEnvelope = new SerializableEnvelope
                {
                    MessageId = envelope.MessageId,
                    Headers = envelope.Headers ?? new MessageHeaders(),
                    Routing = envelope.Routing,
                    PayloadJson = JsonSerializer.Serialize(envelope.Payload, _payloadOptions)
                };

                var json = JsonSerializer.Serialize(serializableEnvelope, _jsonOptions);
                var serializedSizeBytes = System.Text.Encoding.UTF8.GetByteCount(json);
                if (serializedSizeBytes > ContextSizeValidator.DEFAULT_MAX_CONTEXT_SIZE_BYTES)
                {
                    validationResult.IsValid = false;
                    validationResult.Violations.Add(
                        $"Serialized size ({serializedSizeBytes} bytes) exceeds maximum allowed size ({ContextSizeValidator.DEFAULT_MAX_CONTEXT_SIZE_BYTES} bytes)");
                }
            }

            if (envelope.Routing != null)
            {
                if (envelope.Routing.TimeToLiveSeconds.HasValue && envelope.Routing.TimeToLiveSeconds.Value <= 0)
                {
                    validationResult.IsValid = false;
                    validationResult.Violations.Add("TimeToLiveSeconds must be positive");
                }

                if (envelope.Routing.Priority < 0)
                {
                    validationResult.IsValid = false;
                    validationResult.Violations.Add("Priority cannot be negative");
                }
            }

            return validationResult;
        }
    }

        public class SerializableEnvelope
    {
        public string MessageId { get; set; } = string.Empty;
        public MessageHeaders Headers { get; set; } = new();
        public MessageRoutingInfo? Routing { get; set; }
        public string? PayloadJson { get; set; }
    }

        public class EnvelopeValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Violations { get; } = new();

        public override string ToString()
        {
            return IsValid ? "Valid" : $"Invalid: {string.Join("; ", Violations)}";
        }
    }
}
