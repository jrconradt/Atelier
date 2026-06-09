using System.Collections.Concurrent;
using Atelier.Framework.Outcomes;
using Atelier.Framework.StateMachine.Service;

namespace Atelier.Framework.StateMachine;

public static class StateMachineSnapshotMigrator
{
    private static readonly ConcurrentDictionary<int, Func<StateMachineSnapshot, StateMachineSnapshot>> Steps = new();

    public static void RegisterStep(int fromVersion, Func<StateMachineSnapshot, StateMachineSnapshot> upgrade)
    {
        ArgumentNullException.ThrowIfNull(upgrade);
        Steps[fromVersion] = upgrade;
    }

    public static Outcome<StateMachineSnapshot> Migrate(StateMachineSnapshot snapshot, int targetVersion)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Version > targetVersion)
        {
            return Outcome<StateMachineSnapshot>.Failure();
        }

        var current = Clone(snapshot);

        while (current.Version < targetVersion)
        {
            var fromVersion = current.Version;

            if (!Steps.TryGetValue(fromVersion, out var upgrade))
            {
                return Outcome<StateMachineSnapshot>.Failure();
            }

            var next = upgrade(current);

            if (next is null)
            {
                return Outcome<StateMachineSnapshot>.Failure();
            }

            if (next.Version != fromVersion + 1)
            {
                return Outcome<StateMachineSnapshot>.Failure();
            }

            current = next;
        }

        return Outcome<StateMachineSnapshot>.Success(current);
    }

    private static StateMachineSnapshot Clone(StateMachineSnapshot snapshot)
    {
        return new StateMachineSnapshot
        {
            InstanceId = snapshot.InstanceId,
            Type = snapshot.Type,
            CurrentState = snapshot.CurrentState,
            Configuration = snapshot.Configuration,
            LastTransition = snapshot.LastTransition,
            CreatedAt = snapshot.CreatedAt,
            Data = snapshot.Data,
            SnapshotAt = snapshot.SnapshotAt,
            Version = snapshot.Version
        };
    }
}
