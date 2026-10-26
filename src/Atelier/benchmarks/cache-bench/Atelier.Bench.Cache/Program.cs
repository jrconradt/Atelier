using System.Collections.Concurrent;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Atelier.Facilities.Cache;
using Atelier.Framework.Outcomes;

var summary = BenchmarkRunner.Run<CacheBench>();
BenchmarkResultEmitter.Emit(summary);

[MemoryDiagnoser]
public class CacheBench
{
    private readonly InMemoryCache _cache = new();

    private readonly CacheKey _presentKey = new()
    {
        Key = "atelier:integration:user:42",
        Namespace = "sessions"
    };

    private readonly CacheKey _absentKey = new()
    {
        Key = "atelier:integration:user:absent",
        Namespace = "sessions"
    };

    private readonly CacheValue _value = new()
    {
        Value = "{\"id\":42,\"name\":\"Atelier\"}",
        Ttl = TimeSpan.FromMinutes(5)
    };

    private readonly CacheKey _invalidKey = new() { Key = "   " };

    [GlobalSetup]
    public async Task Setup()
    {
        await _cache.SetAsync(_presentKey,
                              _value).ConfigureAwait(false);
    }

    [Benchmark]
    [BenchmarkCategory("Cache")]
    public async Task<bool> SetHotPath()
    {
        var outcome = await _cache.SetAsync(_presentKey,
                                            _value).ConfigureAwait(false);
        return outcome.IsSuccess;
    }

    [Benchmark]
    [BenchmarkCategory("Cache")]
    public async Task<bool> GetHit()
    {
        var outcome = await _cache.GetAsync(_presentKey).ConfigureAwait(false);
        return outcome.Data!.Found;
    }

    [Benchmark]
    [BenchmarkCategory("Cache")]
    public async Task<bool> GetMiss()
    {
        var outcome = await _cache.GetAsync(_absentKey).ConfigureAwait(false);
        return outcome.Data!.Found;
    }

    [Benchmark]
    [BenchmarkCategory("Cache")]
    public async Task<bool> RemoveHotPath()
    {
        await _cache.SetAsync(_absentKey,
                              _value).ConfigureAwait(false);
        var outcome = await _cache.RemoveAsync(_absentKey).ConfigureAwait(false);
        var lookup = await _cache.GetAsync(_absentKey).ConfigureAwait(false);
        return outcome.IsSuccess && !lookup.Data!.Found;
    }

    [Benchmark]
    [BenchmarkCategory("Cache")]
    public async Task<bool> GetInvalidKeyRejected()
    {
        var outcome = await _cache.GetAsync(_invalidKey).ConfigureAwait(false);
        return outcome.IsSuccess;
    }
}

internal sealed class InMemoryCache : ICache
{
    private readonly ConcurrentDictionary<string, CacheValue> _entries = new(StringComparer.Ordinal);

    public Task<Outcome<CacheLookup>> GetAsync(
        CacheKey key,
        CancellationToken cancellationToken = default)
    {
        if (IsInvalid(key))
        {
            return Task.FromResult(Outcome<CacheLookup>.Failure());
        }

        if (_entries.TryGetValue(key.Composite(), out var stored))
        {
            return Task.FromResult(Outcome<CacheLookup>.Success(new CacheLookup
            {
                Found = true,
                Value = stored
            }));
        }

        return Task.FromResult(Outcome<CacheLookup>.Success(new CacheLookup
        {
            Found = false
        }));
    }

    public Task<Outcome> SetAsync(
        CacheKey key,
        CacheValue value,
        CancellationToken cancellationToken = default)
    {
        if (IsInvalid(key))
        {
            return Task.FromResult(Outcome.Failure());
        }

        if (value is null)
        {
            return Task.FromResult(Outcome.Failure());
        }

        _entries[key.Composite()] = value;
        return Task.FromResult(Outcome.Success());
    }

    public Task<Outcome> RemoveAsync(
        CacheKey key,
        CancellationToken cancellationToken = default)
    {
        if (IsInvalid(key))
        {
            return Task.FromResult(Outcome.Failure());
        }

        _entries.TryRemove(key.Composite(), out _);
        return Task.FromResult(Outcome.Success());
    }

    private static bool IsInvalid(CacheKey key)
    {
        if (key is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(key.Key))
        {
            return true;
        }

        return false;
    }
}

public static class BenchmarkResultEmitter
{
    public static void Emit(Summary summary)
    {
        foreach (var report in summary.Reports)
        {
            var descriptor = report.BenchmarkCase.Descriptor;
            var statistics = report.ResultStatistics;
            var allocated = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase) ?? 0L;

            var result = new
            {
                Category = descriptor.Categories.Length > 0 ? descriptor.Categories[0] : string.Empty,
                ClassName = descriptor.Type.Name,
                MethodName = descriptor.WorkloadMethod.Name,
                Mean = statistics?.Mean ?? 0.0,
                StdDev = statistics?.StandardDeviation ?? 0.0,
                Allocated = allocated,
                Unit = "ns",
                Tolerance = 0.10
            };

            Console.WriteLine(JsonSerializer.Serialize(result));
        }
    }
}
