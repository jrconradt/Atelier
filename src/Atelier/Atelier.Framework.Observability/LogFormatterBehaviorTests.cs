using System.Text.Json;
using Atelier.Framework.Context;
using Atelier.Framework.Observability.Formatting;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Observability;

public static class LogFormatterBehaviorTests
{
    private static LoggingContext MakeContext()
    {
        return new LoggingContext(
            global::Atelier.Framework.Context.Context.CreateSystemContext("formatting"),
            "order placed",
            new InvalidOperationException("boom"),
            new Dictionary<string, object>
            {
                ["OrderId"] = 7,
                ["Status"] = "shipped"
            },
            LogLevel.Information);
    }

    [GeneratedTest("Observability/Json-Formatter-Emits-Object", "global::Atelier.Framework.Observability.Formatting.JsonFormatter")]
    public static void JsonFormatterEmitsParsableObjectWithMessageAndValues()
    {
        var formatted = new JsonFormatter().Format(MakeContext());

        using var document = JsonDocument.Parse(formatted);
        var root = document.RootElement;

        if (root.GetProperty("Message").GetString() != "order placed")
        {
            throw new InvalidOperationException($"json message mismatch: {formatted}");
        }
        if (!root.TryGetProperty("Values", out var values)
            || values.GetProperty("OrderId").GetInt32() != 7)
        {
            throw new InvalidOperationException($"json values mismatch: {formatted}");
        }
        if (!root.TryGetProperty("Exception", out var exception)
            || exception.GetProperty("Message").GetString() != "boom")
        {
            throw new InvalidOperationException($"json exception mismatch: {formatted}");
        }
    }

    [GeneratedTest("Observability/Compact-Formatter-Single-Line", "global::Atelier.Framework.Observability.Formatting.CompactFormatter")]
    public static void CompactFormatterRendersSingleLineWithMessageAndValues()
    {
        var formatted = new CompactFormatter().Format(MakeContext());

        if (formatted.Contains('\n'))
        {
            throw new InvalidOperationException($"compact output spanned multiple lines: {formatted}");
        }
        if (!formatted.Contains("order placed"))
        {
            throw new InvalidOperationException($"compact output missing message: {formatted}");
        }
        if (!formatted.Contains("OrderId=7"))
        {
            throw new InvalidOperationException($"compact output missing value: {formatted}");
        }
        if (!formatted.Contains("Exception: InvalidOperationException"))
        {
            throw new InvalidOperationException($"compact output missing exception: {formatted}");
        }
    }

    [GeneratedTest("Observability/PlainText-Formatter-Lines", "global::Atelier.Framework.Observability.Formatting.PlainTextFormatter")]
    public static void PlainTextFormatterRendersLinePerValue()
    {
        var formatted = new PlainTextFormatter().Format(MakeContext());
        var lines = formatted.Split(Environment.NewLine);

        if (!lines[0].Contains("order placed"))
        {
            throw new InvalidOperationException($"plain text first line missing message: {formatted}");
        }
        if (!Array.Exists(lines, line => line.Contains("[OrderId]") && line.Contains("7")))
        {
            throw new InvalidOperationException($"plain text missing value line: {formatted}");
        }
        if (!Array.Exists(lines, line => line.Contains("[InvalidOperationException]") && line.Contains("boom")))
        {
            throw new InvalidOperationException($"plain text missing exception line: {formatted}");
        }
    }

    [GeneratedTest("Observability/Custom-Formatter-Delegates", "global::Atelier.Framework.Observability.Formatting.CustomFormatter")]
    public static void CustomFormatterDelegatesToSuppliedFunction()
    {
        var formatter = new CustomFormatter(ctx => $"custom:{ctx.Message}");
        var formatted = formatter.Format(MakeContext());

        if (formatted != "custom:order placed")
        {
            throw new InvalidOperationException($"custom formatter did not delegate: {formatted}");
        }
    }
}
