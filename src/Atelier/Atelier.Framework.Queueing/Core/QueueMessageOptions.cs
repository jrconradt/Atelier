using Atelier.Framework.Attributes;
using Atelier.Framework.Properties;

namespace Atelier.Framework.Queueing.Core;

[ContractAttribute("QueueMessageOptions", Version = "1.0", Namespace = "Framework.Queueing.Core")]
public class QueueMessageOptions
{
        public int Priority { get; set; } = 0;

        public int? TimeToLiveSeconds { get; set; }

        public int MaxRetries { get; set; } = 3;

        public TimeSpan? Delay { get; set; }

        public DateTime? ScheduledFor { get; set; }

        public string? CorrelationId { get; set; }

        public string? TraceId { get; set; }

        public string? SpanId { get; set; }

        public string? ParentSpanId { get; set; }

        public QueueMessageMetadata Metadata { get; set; } = new();

        public Dictionary<string, string> Headers { get; set; } = new();

        public bool ProcessImmediately { get; set; } = false;

        public bool PersistToDisk { get; set; } = false;

        public bool Compress { get; set; } = false;

        public string? CompressionAlgorithm { get; set; }

        public bool Encrypt { get; set; } = false;

        public string? EncryptionAlgorithm { get; set; }
}
