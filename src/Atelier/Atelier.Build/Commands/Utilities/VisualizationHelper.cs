using System.Globalization;

namespace Atelier.Build.Commands.Utilities;

public static class VisualizationHelper
{
    private static IReadOnlyList<double> WindowValues(IReadOnlyList<double> values, int maxLength)
    {
        if (values.Count <= maxLength)
        {
            return values;
        }

        return values.Skip(values.Count - maxLength).ToList();
    }

    public static string GenerateSparkline(IReadOnlyList<double> values, int maxLength = 8)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        var chars = new[] { '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█' };
        var window = WindowValues(values, maxLength);
        var min = window.Min();
        var max = window.Max();
        var range = max - min;

        if (range == 0)
        {
            return new string(chars[4], window.Count);
        }

        return string.Join(string.Empty, window.Select(v =>
        {
            var normalized = (v - min) / range;
            var index = (int)(normalized * (chars.Length - 1));
            return chars[Math.Clamp(index, 0, chars.Length - 1)];
        }));
    }

    public static string GenerateSparklineWithRange(IReadOnlyList<double> values, int maxLength = 8)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        var window = WindowValues(values, maxLength);
        var sparkline = GenerateSparkline(values, maxLength);
        var min = window.Min();
        var max = window.Max();
        return $"{sparkline} [{min.ToString("F2", CultureInfo.InvariantCulture)}..{max.ToString("F2", CultureInfo.InvariantCulture)}]";
    }

    public static double CalculateTrend(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        var n = values.Count;
        var sumX = Enumerable.Range(0, n).Sum();
        var sumY = values.Sum();
        var sumXY = Enumerable.Range(0, n).Sum(i => i * values[i]);
        var sumX2 = Enumerable.Range(0, n).Sum(i => i * i);

        var denominator = n * sumX2 - sumX * sumX;
        if (denominator == 0)
        {
            return 0;
        }

        return (n * sumXY - sumX * sumY) / denominator;
    }

    public static string GetTrendArrow(double trend)
    {
        if (trend > 0.01)
        {
            return "↗";
        }
        if (trend < -0.01)
        {
            return "↘";
        }
        return "→";
    }

    public static string GetTrendLabel(double trend)
    {
        if (trend > 0.01)
        {
            return "up";
        }
        if (trend < -0.01)
        {
            return "down";
        }
        return "flat";
    }

    public static string GetTrendMarkup(double trend)
    {
        var arrow = GetTrendArrow(trend);
        var label = GetTrendLabel(trend);
        var color = trend > 0 ? "green" : trend < 0 ? "red" : "yellow";
        return $"[{color}]{arrow} {label}[/]";
    }

    public static string FormatDuration(double seconds)
    {
        if (seconds < 1)
        {
            return $"{(int)(seconds * 1000)}ms";
        }
        if (seconds < 60)
        {
            return $"{seconds.ToString("F2", CultureInfo.InvariantCulture)}s";
        }
        if (seconds < 3600)
        {
            return $"{(int)(seconds / 60)}m {(int)(seconds % 60)}s";
        }
        return $"{(int)(seconds / 3600)}h {(int)((seconds % 3600) / 60)}m";
    }

    public static string FormatTimeAgo(DateTime timestamp)
    {
        var elapsed = DateTime.UtcNow - timestamp;

        if (elapsed.TotalMinutes < 1)
        {
            return "just now";
        }
        if (elapsed.TotalMinutes < 60)
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }
        if (elapsed.TotalHours < 24)
        {
            return $"{(int)elapsed.TotalHours}h ago";
        }
        if (elapsed.TotalDays < 7)
        {
            return $"{(int)elapsed.TotalDays}d ago";
        }
        if (elapsed.TotalDays < 30)
        {
            return $"{(int)(elapsed.TotalDays / 7)}w ago";
        }
        return $"{(int)(elapsed.TotalDays / 30)}mo ago";
    }

    public static string FormatPercentage(double percentage, double goodThreshold = 80, double warningThreshold = 60)
    {
        var color = percentage >= goodThreshold ? "green" :
                    percentage >= warningThreshold ? "yellow" : "red";
        var marker = percentage >= goodThreshold ? "✓" :
                    percentage >= warningThreshold ? "⚠" : "✗";
        return $"[{color}]{percentage.ToString("F1", CultureInfo.InvariantCulture)}% {marker}[/]";
    }

    public static string FormatStatus(bool success)
    {
        return success ? "[green]✓ Pass[/]" : "[red]✗ Fail[/]";
    }
}
