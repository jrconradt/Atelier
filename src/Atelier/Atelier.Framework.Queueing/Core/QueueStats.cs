using Atelier.Framework.Attributes;

namespace Atelier.Framework.Queueing.Core;

[ContractAttribute("QueueStats", Version = "1.0", Namespace = "Framework.Queueing.Core")]
public class QueueStats
{
        public int PendingCount { get; set; }

        public int ProcessingCount { get; set; }

        public int CompletedCount { get; set; }

        public int FailedCount { get; set; }

        public double AverageProcessingTimeMs { get; set; }

        public DateTime? LastActivity { get; set; }

        public int QueueDepth => PendingCount + ProcessingCount;

        public double SuccessRate
    {
        get
        {
            var total = CompletedCount + FailedCount;
            return total == 0 ? 0 : (double)CompletedCount / total * 100;
        }
    }
}
