using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using System.Text.Json;
using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Properties;
using Atelier.Framework.StateMachine.Service;

namespace Atelier.Framework.StateMachine.Services;

[Infrastructure(InfrastructureLifetime.Singleton)]
public sealed class FileStateMachinePersistence : IStateMachinePersistence
{
    private const string SNAPSHOT_EXTENSION = ".snapshot.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private readonly string _rootDirectory;
    private readonly ConcurrentDictionary<string, byte> _knownInstances = new();

    public FileStateMachinePersistence()
    {
        _rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "state-machine-snapshots");
        Directory.CreateDirectory(_rootDirectory);

        foreach (var path in Directory.EnumerateFiles(_rootDirectory, $"*{SNAPSHOT_EXTENSION}"))
        {
            var instanceId = DecodeInstanceId(Path.GetFileName(path));
            if (instanceId is not null)
            {
                _knownInstances[instanceId] = 0;
            }
        }
    }

    public async Task<Outcome> SaveSnapshotAsync(
        StateMachineSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (snapshot is null)
        {
            return Outcome.Failure();
        }

        if (string.IsNullOrWhiteSpace(snapshot.InstanceId))
        {
            return Outcome.Failure();
        }

        var record = SnapshotRecord.FromSnapshot(snapshot);
        var json = JsonSerializer.Serialize(record, SerializerOptions);
        var path = PathFor(snapshot.InstanceId);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
        _knownInstances[snapshot.InstanceId] = 0;

        return Outcome.Success();
    }

    public async Task<Outcome<StateMachineSnapshot>> LoadSnapshotAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return Outcome<StateMachineSnapshot>.Failure();
        }

        var path = PathFor(instanceId);
        if (!File.Exists(path))
        {
            return Outcome<StateMachineSnapshot>.Failure();
        }

        SnapshotRecord? record;
        StateMachineSnapshot snapshot;
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            record = JsonSerializer.Deserialize<SnapshotRecord>(json, SerializerOptions);
            if (record is null)
            {
                return Outcome<StateMachineSnapshot>.Failure();
            }

            snapshot = record.ToSnapshot();
        }
        catch (JsonException)
        {
            return Outcome<StateMachineSnapshot>.Failure();
        }
        catch (FormatException)
        {
            return Outcome<StateMachineSnapshot>.Failure();
        }
        catch (OverflowException)
        {
            return Outcome<StateMachineSnapshot>.Failure();
        }
        catch (IOException)
        {
            return Outcome<StateMachineSnapshot>.Failure();
        }

        return Outcome<StateMachineSnapshot>.Success(snapshot);
    }

    public async Task<Outcome<IEnumerable<StateMachineSnapshot>>> GetAllSnapshotsAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshots = new List<StateMachineSnapshot>();

        foreach (var path in Directory.EnumerateFiles(_rootDirectory, $"*{SNAPSHOT_EXTENSION}"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            SnapshotRecord? record;
            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                record = JsonSerializer.Deserialize<SnapshotRecord>(json, SerializerOptions);
            }
            catch (JsonException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            if (record is not null)
            {
                snapshots.Add(record.ToSnapshot());
            }
        }

        return Outcome<IEnumerable<StateMachineSnapshot>>.Success(snapshots);
    }

    public Task<Outcome> DeleteSnapshotAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return Task.FromResult(Outcome.Failure());
        }

        var path = PathFor(instanceId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        _knownInstances.TryRemove(instanceId, out _);
        return Task.FromResult(Outcome.Success());
    }

    public Task<Outcome> CleanupSnapshotsAsync(
        TimeSpan olderThan,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - olderThan;

        foreach (var path in Directory.EnumerateFiles(_rootDirectory, $"*{SNAPSHOT_EXTENSION}"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.GetLastWriteTimeUtc(path) >= cutoff)
            {
                continue;
            }

            File.Delete(path);

            var instanceId = DecodeInstanceId(Path.GetFileName(path));
            if (instanceId is not null)
            {
                _knownInstances.TryRemove(instanceId, out _);
            }
        }

        return Task.FromResult(Outcome.Success());
    }

    private string PathFor(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return Path.Combine(_rootDirectory, $"{EncodeInstanceId(instanceId)}{SNAPSHOT_EXTENSION}");
    }

    private static string EncodeInstanceId(string instanceId)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(instanceId);
        return Convert.ToHexStringLower(bytes);
    }

    private static string? DecodeInstanceId(string fileName)
    {
        if (!fileName.EndsWith(SNAPSHOT_EXTENSION, StringComparison.Ordinal))
        {
            return null;
        }

        var hex = fileName.Substring(0, fileName.Length - SNAPSHOT_EXTENSION.Length);
        if (hex.Length == 0
            || hex.Length % 2 != 0)
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromHexString(hex);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed class SnapshotRecord
    {
        public string InstanceId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string CurrentState { get; set; } = string.Empty;
        public StateMachineConfiguration? Configuration { get; set; }
        public DateTime? LastTransition { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime SnapshotAt { get; set; }
        public int Version { get; set; }
        public List<DataEntry>? Data { get; set; }

        public static SnapshotRecord FromSnapshot(StateMachineSnapshot snapshot)
        {
            List<DataEntry>? entries = null;
            if (snapshot.Data is not null)
            {
                entries = new List<DataEntry>();
                foreach (var kvp in snapshot.Data.ToDictionary())
                {
                    entries.Add(DataEntry.FromValue(kvp.Key, kvp.Value));
                }
            }

            return new SnapshotRecord
            {
                InstanceId = snapshot.InstanceId,
                Type = snapshot.Type,
                CurrentState = snapshot.CurrentState,
                Configuration = snapshot.Configuration,
                LastTransition = snapshot.LastTransition,
                CreatedAt = snapshot.CreatedAt,
                SnapshotAt = snapshot.SnapshotAt,
                Version = snapshot.Version,
                Data = entries
            };
        }

        public StateMachineSnapshot ToSnapshot()
        {
            StateMachineData? data = null;
            if (Data is not null)
            {
                var dictionary = new Dictionary<string, object>();
                foreach (var entry in Data)
                {
                    var value = entry.ToValue();
                    if (value is null)
                    {
                        continue;
                    }
                    dictionary[entry.Key] = value;
                }

                data = (StateMachineData)(TypedPropertyBag)dictionary;
            }

            return new StateMachineSnapshot
            {
                InstanceId = InstanceId,
                Type = Type,
                CurrentState = CurrentState,
                Configuration = Configuration,
                LastTransition = LastTransition,
                CreatedAt = CreatedAt,
                SnapshotAt = SnapshotAt,
                Version = Version,
                Data = data
            };
        }
    }

    private sealed class DataEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;

        public static DataEntry FromValue(string key, object value)
        {
            return value switch
            {
                string s => new DataEntry { Key = key, Kind = "string", Value = s },
                bool b => new DataEntry { Key = key, Kind = "bool", Value = b ? "true" : "false" },
                int i => new DataEntry { Key = key, Kind = "int", Value = $"{i}" },
                long l => new DataEntry { Key = key, Kind = "long", Value = $"{l}" },
                double d => new DataEntry { Key = key, Kind = "double", Value = $"{d:R}" },
                DateTime dt => new DataEntry { Key = key, Kind = "datetime", Value = dt.ToString("O") },
                Guid g => new DataEntry { Key = key, Kind = "guid", Value = $"{g}" },
                _ => new DataEntry { Key = key, Kind = "json", Value = JsonSerializer.Serialize(value, SerializerOptions) }
            };
        }

        public object? ToValue()
        {
            switch (Kind)
            {
                case "string":
                {
                    return Value;
                }
                case "bool":
                {
                    return bool.TryParse(Value, out var b) ? b : null;
                }
                case "int":
                {
                    return int.TryParse(Value, out var i) ? i : null;
                }
                case "long":
                {
                    return long.TryParse(Value, out var l) ? l : null;
                }
                case "double":
                {
                    return double.TryParse(
                        Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var d) ? d : null;
                }
                case "datetime":
                {
                    return DateTime.TryParse(
                        Value,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var dt) ? dt : null;
                }
                case "guid":
                {
                    return Guid.TryParse(Value, out var g) ? g : null;
                }
                default:
                {
                    try
                    {
                        return JsonSerializer.Deserialize<JsonElement>(Value, SerializerOptions);
                    }
                    catch (JsonException)
                    {
                        return null;
                    }
                }
            }
        }
    }
}
