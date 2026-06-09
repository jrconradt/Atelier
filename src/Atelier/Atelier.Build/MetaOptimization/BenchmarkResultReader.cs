using System.Text.Json;

namespace Atelier.Build.MetaOptimization;

public static class BenchmarkResultReader
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static BenchmarkResults FromLines(
        IReadOnlyList<string> lines,
        string subsystem,
        DateTime? timestamp = null)
    {
        var results = new Dictionary<string, BenchmarkResult>();

        foreach (var raw in lines)
        {
            var line = raw.TrimStart();
            if (line.Length == 0 || line[0] != '{')
            {
                continue;
            }

            BenchmarkResult? result;
            try
            {
                result = JsonSerializer.Deserialize<BenchmarkResult>(line, _options);
            }
            catch (JsonException)
            {
                continue;
            }

            if (result is not null)
            {
                results[result.FullName] = result;
            }
        }

        return new BenchmarkResults
        {
            Subsystem = subsystem,
            Timestamp = timestamp ?? DateTime.UtcNow,
            Results = results,
            Platform = PlatformInfo.Detect()
        };
    }

    public static BenchmarkResults FromFile(string jsonlPath, string subsystem)
    {
        if (!File.Exists(jsonlPath))
        {
            throw new FileNotFoundException($"Benchmark results not found: {jsonlPath}");
        }

        return FromLines(File.ReadAllLines(jsonlPath), subsystem, File.GetLastWriteTimeUtc(jsonlPath));
    }

        public static int WriteJsonl(IReadOnlyList<string> stdoutLines, string jsonlPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(jsonlPath)!);

        var resultLines = stdoutLines.Where(l =>
        {
            var trimmed = l.TrimStart();
            return trimmed.Length > 0 && trimmed[0] == '{';
        });

        File.WriteAllLines(jsonlPath, resultLines);
        return resultLines.Count();
    }
}
