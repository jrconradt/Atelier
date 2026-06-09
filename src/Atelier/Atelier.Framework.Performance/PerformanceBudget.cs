using Atelier.Framework.Attributes;

namespace Atelier.Framework.Performance;

[Contract("PerformanceBudget", Version = "1.0")]
public class PerformanceBudget
{
    public string Component { get; set; } = string.Empty;
    public double MaxP95LatencyMs { get; set; }
    public double MaxP99LatencyMs { get; set; }
    public double MaxErrorRate { get; set; }
}

[Contract("BudgetBreach", Version = "1.0")]
public class BudgetBreach
{
    public string Component { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public double Threshold { get; set; }
    public double ActualValue { get; set; }
    public AlertSeverity Severity { get; set; }
}

internal static class BudgetEvaluation
{
    public static List<BudgetBreach> Evaluate(
        PerformanceBudget budget,
        ComponentMetrics metrics)
    {
        var breaches = new List<BudgetBreach>();

        if (budget.MaxP95LatencyMs > 0
            && metrics.P95LatencyMs > budget.MaxP95LatencyMs)
        {
            breaches.Add(new BudgetBreach
            {
                Component = budget.Component,
                Metric = "p95_latency_ms",
                Threshold = budget.MaxP95LatencyMs,
                ActualValue = metrics.P95LatencyMs,
                Severity = AlertSeverity.Warning
            });
        }

        if (budget.MaxP99LatencyMs > 0
            && metrics.P99LatencyMs > budget.MaxP99LatencyMs)
        {
            breaches.Add(new BudgetBreach
            {
                Component = budget.Component,
                Metric = "p99_latency_ms",
                Threshold = budget.MaxP99LatencyMs,
                ActualValue = metrics.P99LatencyMs,
                Severity = AlertSeverity.Critical
            });
        }

        if (budget.MaxErrorRate > 0
            && metrics.ErrorRate > budget.MaxErrorRate)
        {
            breaches.Add(new BudgetBreach
            {
                Component = budget.Component,
                Metric = "error_rate",
                Threshold = budget.MaxErrorRate,
                ActualValue = metrics.ErrorRate,
                Severity = AlertSeverity.Critical
            });
        }

        return breaches;
    }
}
