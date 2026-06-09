namespace Atelier.Framework.Performance;

internal static class MetricCalculations
{
    public static double GetPercentile(
        List<double> sortedValues,
        double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        index = Math.Max(0, Math.Min(sortedValues.Count - 1, index));
        return sortedValues[index];
    }

    public static ComponentMetrics CalculateComponentMetrics(
        string componentName,
        List<PerformanceMetric> metrics)
    {
        var latencyMetrics = metrics
            .Where(m => m.Type == MetricType.Latency)
            .Select(m => m.Value)
            .OrderBy(v => v)
            .ToList();

        var errorMetrics = metrics
            .Where(m => m.Type == MetricType.ErrorRate)
            .ToList();

        var memoryMetrics = metrics
            .Where(m => m.Type == MetricType.Memory)
            .ToList();

        var latencyCount = latencyMetrics.Count;
        var hasLatency = latencyCount > 0;

        return new ComponentMetrics
        {
            ComponentName = componentName,
            AverageLatencyMs = hasLatency ? latencyMetrics.Average() : 0,
            P50LatencyMs = hasLatency ? GetPercentile(latencyMetrics, 0.50) : 0,
            P95LatencyMs = hasLatency ? GetPercentile(latencyMetrics, 0.95) : 0,
            P99LatencyMs = hasLatency ? GetPercentile(latencyMetrics, 0.99) : 0,
            TotalOperations = latencyCount,
            OperationsPerSecond = latencyCount / 60.0,
            ErrorCount = errorMetrics.Count,
            ErrorRate = errorMetrics.Count > 0 ? errorMetrics.Average(m => m.Value) : 0,
            MemoryAllocatedBytes = memoryMetrics.Count > 0 ? (long)memoryMetrics[^1].Value : 0
        };
    }
}
