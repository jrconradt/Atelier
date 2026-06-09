using System.Globalization;
using System.Xml.Linq;

namespace Atelier.Build.Pipeline;

public static class TrxResultReader
{
    private static readonly XNamespace _trxNs = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static TestProjectResult? Read(string trxPath)
    {
        if (!File.Exists(trxPath))
        {
            return null;
        }

        try
        {
            var doc = XDocument.Load(trxPath);

            var counters = doc.Descendants(_trxNs + "Counters").FirstOrDefault();
            if (counters is null)
            {
                return null;
            }

            var total = (int?)counters.Attribute("total") ?? 0;
            var passed = (int?)counters.Attribute("passed") ?? 0;
            var failed = (int?)counters.Attribute("failed") ?? 0;

            var executed = (int?)counters.Attribute("executed") ?? (passed + failed);
            var skipped = Math.Max(0, total - executed);

            double durationSeconds = 0;
            foreach (var unit in doc.Descendants(_trxNs + "UnitTestResult"))
            {
                var d = unit.Attribute("duration")?.Value;
                if (d is not null && TimeSpan.TryParse(d, CultureInfo.InvariantCulture, out var span))
                {
                    durationSeconds += span.TotalSeconds;
                }
            }

            return new TestProjectResult
            {
                Total = total,
                Passed = passed,
                Failed = failed,
                Skipped = skipped,
                Duration = durationSeconds
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: failed to parse TRX file {trxPath}: {ex.Message}");
            return null;
        }
    }
}
