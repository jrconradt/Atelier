using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Atelier.Framework.EventStream.Configuration;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Microsoft.Extensions.Options;

namespace Atelier.Framework.EventStream.Storage;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class EventStreamOffsetStore : IAtelier, IEventStreamOffsetStore
{
    [Requisite] private readonly IOptions<EventStreamOptions> _options = null!;

    private readonly ConcurrentDictionary<(string ConsumerGroup, string Topic), long> _offsets = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _topicsByGroup = new();
    private readonly ConcurrentDictionary<string, Task> _persistChains = new();

    [Operation("InitializeAsync")]
    public async Task<Outcome> InitializeAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        var directory = _options.Value.OffsetStoreDirectory;
        Directory.CreateDirectory(directory);

        var loadedGroups = 0;
        var loadedTopics = 0;

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (!TryDecodeGroupName(Path.GetFileNameWithoutExtension(file), out var consumerGroup))
            {
                Observe(LogLevel.Warning, values: [("File", file), ("Reason", "UNDECODABLE_GROUP_NAME")]);
                continue;
            }

            Dictionary<string, long>? topicOffsets;

            try
            {
                await using var stream = File.OpenRead(file);
                topicOffsets = await JsonSerializer
                    .DeserializeAsync<Dictionary<string, long>>(stream, cancellationToken: ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Observe(LogLevel.Error, ex, values: [("File", file), ("ConsumerGroup", consumerGroup)]);
                continue;
            }

            if (topicOffsets is null)
            {
                continue;
            }

            loadedGroups++;

            foreach (var (topic, offset) in topicOffsets)
            {
                _offsets[(consumerGroup, topic)] = offset;
                IndexTopic(consumerGroup, topic);
                loadedTopics++;
            }
        }

        Observe(LogLevel.Information, values: [("Directory", directory), ("LoadedGroups", loadedGroups), ("LoadedTopics", loadedTopics)]);

        return Outcome.Success();
    }

    [Operation("CommitOffsetAsync")]
    public async Task<Outcome> CommitOffsetAsync(
        string consumerGroup,
        string topic,
        long offset,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "ConsumerGroup", consumerGroup);

        var key = (consumerGroup, topic);
        var committed = _offsets.AddOrUpdate(key, offset, (_, existing) => Math.Max(existing, offset));

        if (committed > offset)
        {
            Observe(LogLevel.Warning, values: [("Reason", "Offset regresses below committed offset"), ("ConsumerGroup", consumerGroup), ("Topic", topic), ("Offset", offset), ("CommittedOffset", committed)]);

            return Outcome.Failure();
        }

        IndexTopic(consumerGroup, topic);
        await PersistGroupAsync(consumerGroup, ct).ConfigureAwait(false);

        Observe(LogLevel.Debug, values: [("ConsumerGroup", consumerGroup), ("Topic", topic), ("Offset", offset)]);

        return Outcome.Success();
    }

    [Operation("CommitOffsetsAsync")]
    public async Task<Outcome<int>> CommitOffsetsAsync(
        string consumerGroup,
        IReadOnlyDictionary<string, long> topicOffsets,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Outcome<int>.Failure();
        }

        if (consumerGroup is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", $"{nameof(consumerGroup)} was null")]);
            return Outcome<int>.Failure();
        }

        if (topicOffsets is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", $"{nameof(topicOffsets)} was null"), ("ConsumerGroup", consumerGroup)]);
            return Outcome<int>.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "ConsumerGroup", consumerGroup);

        var regressed = new List<string>();
        var committedCount = 0;

        foreach (var (topic, offset) in topicOffsets)
        {
            var key = (consumerGroup, topic);
            var committed = _offsets.AddOrUpdate(key, offset, (_, existing) => Math.Max(existing, offset));

            if (committed > offset)
            {
                regressed.Add($"{topic}@{offset}<{committed}");
                continue;
            }

            IndexTopic(consumerGroup, topic);
            committedCount++;
        }

        await PersistGroupAsync(consumerGroup, ct).ConfigureAwait(false);

        if (regressed.Count > 0)
        {
            Observe(LogLevel.Warning, values: [("Reason", "One or more offsets regressed below committed high-water-mark"), ("ConsumerGroup", consumerGroup), ("Committed", committedCount), ("Regressed", string.Join(", ", regressed))]);

            return Outcome<int>.Failure();
        }

        Observe(LogLevel.Debug, values: [("ConsumerGroup", consumerGroup), ("Committed", committedCount)]);

        return Outcome<int>.Success(committedCount);
    }

    [Operation("GetOffsetAsync")]
    public Task<Outcome<long>> GetOffsetAsync(
        string consumerGroup,
        string topic,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<long>.Failure());
        }

        if (consumerGroup is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", $"{nameof(consumerGroup)} was null")]);
            return Task.FromResult(Outcome<long>.Failure());
        }

        if (topic is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", $"{nameof(topic)} was null"), ("ConsumerGroup", consumerGroup)]);
            return Task.FromResult(Outcome<long>.Failure());
        }

        var key = (consumerGroup, topic);
        var offset = _offsets.TryGetValue(key, out var value) ? value : 0L;
        return Task.FromResult(Outcome<long>.Success(offset));
    }

    [Operation("GetAllOffsetsForConsumerAsync")]
    public Task<Outcome<Dictionary<string, long>>> GetAllOffsetsForConsumerAsync(
        string consumerGroup,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<Dictionary<string, long>>.Failure());
        }

        if (consumerGroup is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", $"{nameof(consumerGroup)} was null")]);
            return Task.FromResult(Outcome<Dictionary<string, long>>.Failure());
        }

        var offsets = new Dictionary<string, long>();

        if (_topicsByGroup.TryGetValue(consumerGroup, out var topics))
        {
            foreach (var topic in topics.Keys)
            {
                if (_offsets.TryGetValue((consumerGroup, topic), out var value))
                {
                    offsets[topic] = value;
                }
            }
        }

        return Task.FromResult(Outcome<Dictionary<string, long>>.Success(offsets));
    }

    [Operation("RemoveOffsetAsync")]
    public async Task<Outcome> RemoveOffsetAsync(
        string consumerGroup,
        string topic,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        if (consumerGroup is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", $"{nameof(consumerGroup)} was null")]);
            return Outcome.Failure();
        }

        if (topic is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", $"{nameof(topic)} was null"), ("ConsumerGroup", consumerGroup)]);
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "ConsumerGroup", consumerGroup);

        var removed = _offsets.TryRemove((consumerGroup, topic), out _);

        if (_topicsByGroup.TryGetValue(consumerGroup, out var topics))
        {
            topics.TryRemove(topic, out _);
        }

        await PersistGroupAsync(consumerGroup, ct).ConfigureAwait(false);

        Observe(LogLevel.Debug, values: [("ConsumerGroup", consumerGroup), ("Topic", topic), ("Removed", removed)]);

        return Outcome.Success();
    }

    [Operation("DeleteOffsetsForConsumerAsync")]
    public async Task<Outcome<int>> DeleteOffsetsForConsumerAsync(
        string consumerGroup,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Outcome<int>.Failure();
        }

        if (consumerGroup is null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", $"{nameof(consumerGroup)} was null")]);
            return Outcome<int>.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "ConsumerGroup", consumerGroup);

        var deleted = 0;

        if (_topicsByGroup.TryRemove(consumerGroup, out var topics))
        {
            foreach (var topic in topics.Keys)
            {
                if (_offsets.TryRemove((consumerGroup, topic), out _))
                {
                    deleted++;
                }
            }
        }

        var file = GroupFilePath(consumerGroup);
        if (File.Exists(file))
        {
            File.Delete(file);
        }

        await Task.CompletedTask.ConfigureAwait(false);

        Observe(LogLevel.Information, values: [("ConsumerGroup", consumerGroup), ("DeletedCount", deleted)]);

        return Outcome<int>.Success(deleted);
    }

    private void IndexTopic(string consumerGroup, string topic)
    {
        var topics = _topicsByGroup.GetOrAdd(consumerGroup, _ => new ConcurrentDictionary<string, byte>());
        topics.TryAdd(topic, 0);
    }

    private string GroupFilePath(string consumerGroup)
    {
        var directory = _options.Value.OffsetStoreDirectory;
        var encodedName = Convert.ToHexStringLower(Encoding.UTF8.GetBytes(consumerGroup));
        return Path.Combine(directory, $"{encodedName}.json");
    }

    private static bool TryDecodeGroupName(string stem, out string consumerGroup)
    {
        consumerGroup = string.Empty;

        if (stem.Length == 0
            || (stem.Length & 1) == 1)
        {
            return false;
        }

        try
        {
            consumerGroup = Encoding.UTF8.GetString(Convert.FromHexString(stem));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private Task PersistGroupAsync(string consumerGroup, CancellationToken ct)
    {
        var snapshot = SnapshotGroup(consumerGroup);

        while (true)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var hasPrevious = _persistChains.TryGetValue(consumerGroup, out var previous);

            var next = ChainWriteAsync(gate.Task, previous, consumerGroup, snapshot, ct);

            if (hasPrevious)
            {
                if (_persistChains.TryUpdate(consumerGroup, next, previous!))
                {
                    gate.SetResult();
                    return next;
                }
            }
            else if (_persistChains.TryAdd(consumerGroup, next))
            {
                gate.SetResult();
                return next;
            }
        }
    }

    private Dictionary<string, long> SnapshotGroup(string consumerGroup)
    {
        var snapshot = new Dictionary<string, long>();

        if (_topicsByGroup.TryGetValue(consumerGroup, out var topics))
        {
            foreach (var topic in topics.Keys)
            {
                if (_offsets.TryGetValue((consumerGroup, topic), out var value))
                {
                    snapshot[topic] = value;
                }
            }
        }

        return snapshot;
    }

    private async Task ChainWriteAsync(
        Task gate,
        Task? previous,
        string consumerGroup,
        Dictionary<string, long> snapshot,
        CancellationToken ct)
    {
        await gate.ConfigureAwait(false);

        if (previous is not null)
        {
            try
            {
                await previous.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        await WriteGroupFileAsync(consumerGroup, snapshot, ct).ConfigureAwait(false);
    }

    private async Task WriteGroupFileAsync(
        string consumerGroup,
        Dictionary<string, long> snapshot,
        CancellationToken ct)
    {
        var directory = _options.Value.OffsetStoreDirectory;
        Directory.CreateDirectory(directory);

        var path = GroupFilePath(consumerGroup);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        var stream = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);

        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, cancellationToken: ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
