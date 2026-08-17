using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Contract;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class ContractMigrator : IContractMigrator, IAtelier
{
    [Requisite] protected readonly IContractRegistry _registry = null!;
    private readonly ConcurrentDictionary<string, MigrationEdge> _migrations = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, MigrationEdge>> _adjacency = new();

    public Outcome RegisterMigration<TSource, TTarget>(
        string sourceVersion,
        string targetVersion,
        Func<TSource, TTarget> migrator)
        where TSource : class
        where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(sourceVersion);
        ArgumentNullException.ThrowIfNull(targetVersion);
        ArgumentNullException.ThrowIfNull(migrator);

        var sourceMetadata = _registry.Resolve<TSource>();
        var targetMetadata = _registry.Resolve<TTarget>();

        if (sourceMetadata == null
            || targetMetadata == null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Both source and target contracts must be registered"), ("SourceType", typeof(TSource).FullName ?? typeof(TSource).Name), ("TargetType", typeof(TTarget).FullName ?? typeof(TTarget).Name)]);
            return Outcome.Failure();
        }


        if (!ContractVersion.Equals(
            sourceVersion,
            sourceMetadata.Version))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "sourceVersion does not match the registered source contract version"), ("SourceVersion", sourceVersion), ("RegisteredVersion", sourceMetadata.Version)]);
            return Outcome.Failure();
        }

        if (!ContractVersion.Equals(
            targetVersion,
            targetMetadata.Version))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "targetVersion does not match the registered target contract version"), ("TargetVersion", targetVersion), ("RegisteredVersion", targetMetadata.Version)]);
            return Outcome.Failure();
        }

        var key = BuildMigrationKey(
            sourceMetadata.Name,
            sourceMetadata.Version,
            targetVersion);

        var edge = new MigrationEdge(
            sourceMetadata.Name,
            sourceMetadata.Version,
            targetVersion,
            targetMetadata.IsBackwardCompatible,
            source => migrator((TSource)source));

        if (!_migrations.TryAdd(
            key,
            edge))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "A migration is already registered for this source/target pair"), ("Contract", sourceMetadata.Name), ("SourceVersion", sourceMetadata.Version), ("TargetVersion", targetVersion)]);
            return Outcome.Failure();
        }

        var byName = _adjacency.GetOrAdd(
            sourceMetadata.Name,
            _ => new ConcurrentDictionary<string, MigrationEdge>());
        byName[key] = edge;

        return Outcome.Success();
    }

    public Outcome<TTarget?> Migrate<TSource, TTarget>(
        TSource source,
        string targetVersion,
        bool allowBreaking = false)
        where TSource : class
        where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targetVersion);

        var sourceMetadata = _registry.Resolve<TSource>();
        var targetMetadata = _registry.Resolve<TTarget>();

        if (sourceMetadata == null
            || targetMetadata == null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Both source and target contracts must be registered"), ("SourceType", typeof(TSource).FullName ?? typeof(TSource).Name), ("TargetType", typeof(TTarget).FullName ?? typeof(TTarget).Name)]);
            return Outcome<TTarget?>.Failure();
        }


        if (!ContractVersion.Equals(
            targetVersion,
            targetMetadata.Version))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "targetVersion does not match the registered target contract version"), ("TargetVersion", targetVersion), ("RegisteredVersion", targetMetadata.Version)]);
            return Outcome<TTarget?>.Failure();
        }

        var pathOutcome = ResolvePath(
            sourceMetadata.Name,
            sourceMetadata.Version,
            targetVersion);

        if (!pathOutcome.IsSuccess)
        {
            return Outcome<TTarget?>.Failure();
        }

        if (!allowBreaking
            && !PathIsBackwardCompatible(pathOutcome.Data))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Migration path traverses a breaking schema change; pass allowBreaking to proceed"), ("Contract", sourceMetadata.Name), ("SourceVersion", sourceMetadata.Version), ("TargetVersion", targetVersion)]);
            return Outcome<TTarget?>.Failure();
        }

        var applied = ApplyPath(
            source,
            pathOutcome.Data);

        if (!applied.IsSuccess)
        {
            return Outcome<TTarget?>.Failure();
        }

        if (applied.Data is not TTarget typed)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Migration result is not assignable to the target type"), ("TargetType", typeof(TTarget).FullName ?? typeof(TTarget).Name)]);
            return Outcome<TTarget?>.Failure();
        }

        return Outcome<TTarget?>.Success(typed);
    }

    public Outcome<object?> Migrate(
        object source,
        Type sourceType,
        Type targetType,
        string targetVersion,
        bool allowBreaking = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(targetVersion);

        var sourceMetadata = _registry.Resolve(sourceType);
        var targetMetadata = _registry.Resolve(targetType);

        if (sourceMetadata == null
            || targetMetadata == null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Both source and target contracts must be registered"), ("SourceType", sourceType.FullName ?? sourceType.Name), ("TargetType", targetType.FullName ?? targetType.Name)]);
            return Outcome<object?>.Failure();
        }


        if (!ContractVersion.Equals(
            targetVersion,
            targetMetadata.Version))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "targetVersion does not match the registered target contract version"), ("TargetVersion", targetVersion), ("RegisteredVersion", targetMetadata.Version)]);
            return Outcome<object?>.Failure();
        }

        var pathOutcome = ResolvePath(
            sourceMetadata.Name,
            sourceMetadata.Version,
            targetVersion);

        if (!pathOutcome.IsSuccess)
        {
            return Outcome<object?>.Failure();
        }

        if (!allowBreaking
            && !PathIsBackwardCompatible(pathOutcome.Data))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Migration path traverses a breaking schema change; pass allowBreaking to proceed"), ("Contract", sourceMetadata.Name), ("SourceVersion", sourceMetadata.Version), ("TargetVersion", targetVersion)]);
            return Outcome<object?>.Failure();
        }

        var applied = ApplyPath(
            source,
            pathOutcome.Data);

        if (!applied.IsSuccess)
        {
            return applied;
        }

        if (applied.Data == null
            || !targetType.IsInstanceOfType(applied.Data))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Migration result is not assignable to the target type"), ("TargetType", targetType.FullName ?? targetType.Name)]);
            return Outcome<object?>.Failure();
        }

        return applied;
    }

    public bool CanMigrate(
        string contractName,
        string sourceVersion,
        string targetVersion)
    {
        ArgumentNullException.ThrowIfNull(contractName);
        ArgumentNullException.ThrowIfNull(sourceVersion);
        ArgumentNullException.ThrowIfNull(targetVersion);

        return ResolvePath(
            contractName,
            sourceVersion,
            targetVersion).IsSuccess;
    }

    public bool IsBackwardCompatiblePath(
        string contractName,
        string sourceVersion,
        string targetVersion)
    {
        ArgumentNullException.ThrowIfNull(contractName);
        ArgumentNullException.ThrowIfNull(sourceVersion);
        ArgumentNullException.ThrowIfNull(targetVersion);

        var pathOutcome = ResolvePath(
            contractName,
            sourceVersion,
            targetVersion);

        if (!pathOutcome.IsSuccess)
        {
            return false;
        }

        return PathIsBackwardCompatible(pathOutcome.Data);
    }

    private static bool PathIsBackwardCompatible(List<MigrationEdge> path)
    {
        foreach (var edge in path)
        {
            if (!edge.TargetIsBackwardCompatible)
            {
                return false;
            }
        }

        return true;
    }

    private Outcome<List<MigrationEdge>> ResolvePath(
        string contractName,
        string sourceVersion,
        string targetVersion)
    {

        ArgumentNullException.ThrowIfNull(contractName);
        ArgumentNullException.ThrowIfNull(sourceVersion);
        ArgumentNullException.ThrowIfNull(targetVersion);

        if (string.Equals(sourceVersion,
                         targetVersion,
                         StringComparison.Ordinal))
        {
            return Outcome<List<MigrationEdge>>.Success(new List<MigrationEdge>());
        }

        var directKey = BuildMigrationKey(
            contractName,
            sourceVersion,
            targetVersion);

        if (_migrations.TryGetValue(
            directKey,
            out var directEdge))
        {
            return Outcome<List<MigrationEdge>>.Success(new List<MigrationEdge> { directEdge });
        }

        if (!_adjacency.TryGetValue(
            contractName,
            out var edges))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "No migration path found"), ("Contract", contractName), ("SourceVersion", sourceVersion), ("TargetVersion", targetVersion)]);
            return Outcome<List<MigrationEdge>>.Failure();
        }

        var predecessor = new Dictionary<string, MigrationEdge>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { sourceVersion };
        var frontier = new Queue<string>();
        frontier.Enqueue(sourceVersion);

        var found = false;

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();

            if (string.Equals(current,
                            targetVersion,
                            StringComparison.Ordinal))
            {
                found = true;
                break;
            }

            foreach (var edge in edges.Values
                .OrderBy(e => e.SourceVersion, StringComparer.Ordinal)
                .ThenBy(e => e.TargetVersion, StringComparer.Ordinal))
            {
                if (!string.Equals(edge.SourceVersion,
                                  current,
                                  StringComparison.Ordinal))
                {
                    continue;
                }

                if (!visited.Add(edge.TargetVersion))
                {
                    continue;
                }

                predecessor[edge.TargetVersion] = edge;
                frontier.Enqueue(edge.TargetVersion);
            }
        }

        if (!found
            && !predecessor.ContainsKey(targetVersion))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "No migration path found"), ("Contract", contractName), ("SourceVersion", sourceVersion), ("TargetVersion", targetVersion)]);
            return Outcome<List<MigrationEdge>>.Failure();
        }

        var reversed = new List<MigrationEdge>();
        var cursor = targetVersion;

        while (!string.Equals(cursor,
                            sourceVersion,
                            StringComparison.Ordinal))
        {
            if (!predecessor.TryGetValue(
                cursor,
                out var edge))
            {
                Observe(
                    LogLevel.Warning,
                    null,
                    values: [("Reason", "No migration path found"), ("Contract", contractName), ("SourceVersion", sourceVersion), ("TargetVersion", targetVersion)]);
                return Outcome<List<MigrationEdge>>.Failure();
            }

            reversed.Add(edge);
            cursor = edge.SourceVersion;
        }

        reversed.Reverse();
        return Outcome<List<MigrationEdge>>.Success(reversed);
    }

    private Outcome<object?> ApplyPath(
        object source,
        List<MigrationEdge> path)
    {
        if (path.Count == 0)
        {
            return Outcome<object?>.Success(source);
        }

        var current = source;

        foreach (var edge in path)
        {
            try
            {
                current = edge.Apply(current);
            }
            catch (Exception ex)
            {
                Observe(
                    LogLevel.Error,
                    ex,
                    values: [("Reason", "Migration step failed"), ("Contract", edge.ContractName), ("SourceVersion", edge.SourceVersion), ("TargetVersion", edge.TargetVersion)]);
                return Outcome<object?>.Failure();
            }

            if (current == null)
            {
                Observe(
                    LogLevel.Warning,
                    null,
                    values: [("Reason", "Migration step produced a null contract"), ("Contract", edge.ContractName), ("SourceVersion", edge.SourceVersion), ("TargetVersion", edge.TargetVersion)]);
                return Outcome<object?>.Failure();
            }
        }

        return Outcome<object?>.Success(current);
    }

    private static string BuildMigrationKey(
        string contractName,
        string sourceVersion,
        string targetVersion) =>
        $"{contractName}:{sourceVersion}=>{targetVersion}";

    private readonly struct MigrationEdge
    {
        private readonly Func<object, object?> _transform;

        public MigrationEdge(
            string contractName,
            string sourceVersion,
            string targetVersion,
            bool targetIsBackwardCompatible,
            Func<object, object?> transform)
        {
            ContractName = contractName;
            SourceVersion = sourceVersion;
            TargetVersion = targetVersion;
            TargetIsBackwardCompatible = targetIsBackwardCompatible;
            _transform = transform;
        }

        public string ContractName { get; }
        public string SourceVersion { get; }
        public string TargetVersion { get; }
        public bool TargetIsBackwardCompatible { get; }

        public object? Apply(object source) => _transform(source);
    }
}
