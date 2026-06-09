using System.Diagnostics;
using System.Threading.Channels;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.Performance;

public sealed class ProcessResourceSampler : IAsyncDisposable
{
    private static readonly TimeSpan SAMPLE_INTERVAL = TimeSpan.FromMilliseconds(500);

    private readonly Process _process;
    private readonly Channel<bool> _requests;
    private readonly Task _pump;
    private readonly CancellationTokenSource _shutdown = new();
    private ResourceSample _latest;

    public ProcessResourceSampler()
    {
        _process = Process.GetCurrentProcess();
        _latest = new ResourceSample
        {
            SampledUtc = DateTime.UtcNow,
            TotalProcessorTime = _process.TotalProcessorTime,
            CpuUsagePercent = 0.0,
            WorkingSetBytes = _process.WorkingSet64,
            ThreadCount = _process.Threads.Count,
            HandleCount = _process.HandleCount
        };

        _requests = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });

        _pump = Task.Run(() => PumpAsync(_shutdown.Token));
    }

    public ResourceSample Current
    {
        get
        {
            _requests.Writer.TryWrite(true);
            return Volatile.Read(ref _latest);
        }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _requests.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_requests.Reader.TryRead(out _))
                {
                }

                var previous = Volatile.Read(ref _latest);
                var now = DateTime.UtcNow;
                var elapsedMs = (now - previous.SampledUtc).TotalMilliseconds;

                if (elapsedMs < SAMPLE_INTERVAL.TotalMilliseconds)
                {
                    continue;
                }

                _process.Refresh();
                var currentTotal = _process.TotalProcessorTime;
                var processorDeltaMs = (currentTotal - previous.TotalProcessorTime).TotalMilliseconds;

                var usage = elapsedMs > 0
                    ? processorDeltaMs / elapsedMs * 100.0 / Environment.ProcessorCount
                    : 0.0;

                var next = new ResourceSample
                {
                    SampledUtc = now,
                    TotalProcessorTime = currentTotal,
                    CpuUsagePercent = Math.Clamp(usage, 0.0, 100.0),
                    WorkingSetBytes = _process.WorkingSet64,
                    ThreadCount = _process.Threads.Count,
                    HandleCount = _process.HandleCount
                };

                Volatile.Write(ref _latest, next);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _requests.Writer.TryComplete();
        await _pump.ConfigureAwait(false);
        _shutdown.Dispose();
        _process.Dispose();
    }
}

[Contract("ResourceSample", Version = "1.0")]
public sealed class ResourceSample
{
    public DateTime SampledUtc { get; init; }
    public TimeSpan TotalProcessorTime { get; init; }
    public double CpuUsagePercent { get; init; }
    public long WorkingSetBytes { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }
}
