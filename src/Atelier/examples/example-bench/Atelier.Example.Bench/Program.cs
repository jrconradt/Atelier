using System.Diagnostics;
using System.Text.Json;

const int WARMUP_ITERATIONS = 1_000;
const int MEASURE_ITERATIONS = 100_000;
const int SAMPLE_COUNT = 25;
const int OUTLIER_TRIM = 3;

var benches = new (string Class, string Method, string Category, Action Body)[]
{
    ("ExampleBench", "AddLoop", "Arithmetic", () =>
    {
        int x = 0;
        for (int i = 0; i < 100; i++)
        {
            x += i;
        }
    }),
    ("ExampleBench", "MultiplyLoop", "Arithmetic", () =>
    {
        int x = 1;
        for (int i = 1; i < 100; i++)
        {
            x *= 2;
        }
    }),
    ("ExampleBench", "StringConcat", "Strings", () =>
    {
        var s = string.Empty;
        for (int i = 0; i < 10; i++)
        {
            s += "x";
        }
    }),
};

foreach (var (className, methodName, category, body) in benches)
{
    for (int i = 0; i < WARMUP_ITERATIONS; i++)
    {
        body();
    }

    var samples = new double[SAMPLE_COUNT];
    for (int run = 0; run < samples.Length; run++)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < MEASURE_ITERATIONS; i++)
        {
            body();
        }
        sw.Stop();
        samples[run] = sw.Elapsed.TotalNanoseconds / MEASURE_ITERATIONS;
    }

    Array.Sort(samples);
    var kept = samples[OUTLIER_TRIM..^OUTLIER_TRIM];

    var mean = kept.Average();
    var variance = kept.Select(s => (s - mean) * (s - mean)).Average();
    var stdDev = Math.Sqrt(variance);

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var allocBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < MEASURE_ITERATIONS; i++)
    {
        body();
    }
    var allocAfter = GC.GetAllocatedBytesForCurrentThread();
    var allocated = (allocAfter - allocBefore) / MEASURE_ITERATIONS;

    var result = new
    {
        Category = category,
        ClassName = className,
        MethodName = methodName,
        Mean = mean,
        StdDev = stdDev,
        Allocated = allocated,
        Unit = "ns",
        Tolerance = 0.10
    };

    Console.WriteLine(JsonSerializer.Serialize(result));
}
