using Atelier.Framework.Primitives;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Observability;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Properties;
using Atelier.Framework.Requisitions;
using Atelier.Framework.StateMachine.Interfaces;
using Atelier.Framework.StateMachine.Service;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.StateMachine;

public interface IStateMachineLifecycleInfo
{
    public bool IsTerminal { get; }
    public TimeSpan? AutoCleanupTimeout { get; }
    public DateTime LastActivity { get; }
}

[Infrastructure(InfrastructureLifetime.Transient)]

public partial class StateMachineInstance<T> : IAtelier, IStateMachineInstance, IStateMachineLifecycleInfo where T : IStateMachine
{
    private static readonly IReadOnlyDictionary<string, string> EmptyTags = new Dictionary<string, string>();

    private string _instanceId = string.Empty;
    [Requisite(Required = false)] protected readonly IStateMachineMonitor _monitor = null!;

    private readonly StateTransitionCoordinator _coordinator = new();

    public StateMachineInstance() { }

        public StateMachineInstance<T> Configure(string instanceId)
    {
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        return this;
    }

    private StateMachineConfiguration Configuration { get; set; } = new();

    public string InstanceId => _instanceId;
    public Type Type => typeof(T);
    public IReadOnlyDictionary<string, string> Tags => Configuration.Tags ?? EmptyTags;
    public T StateMachine { get; private set; } = default(T)!;

    private sealed record LifecycleSnapshot(string CurrentState, bool IsHealthy, DateTime? LastTransition);

    private LifecycleSnapshot _lifecycle = new(string.Empty, true, null);

    public string CurrentState
    {
        get => Volatile.Read(ref _lifecycle).CurrentState;
        private set => Volatile.Write(ref _lifecycle, Volatile.Read(ref _lifecycle) with { CurrentState = value });
    }

    public bool IsHealthy
    {
        get => Volatile.Read(ref _lifecycle).IsHealthy;
        private set => Volatile.Write(ref _lifecycle, Volatile.Read(ref _lifecycle) with { IsHealthy = value });
    }

    public DateTime? LastTransition
    {
        get => Volatile.Read(ref _lifecycle).LastTransition;
        private set => Volatile.Write(ref _lifecycle, Volatile.Read(ref _lifecycle) with { LastTransition = value });
    }

    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    public bool IsTerminal => StateMachine is not null && StateMachine.IsTerminal;
    public TimeSpan? AutoCleanupTimeout => Configuration?.AutoCleanupTimeout;
    public DateTime LastActivity => LastTransition ?? CreatedAt;

    [Operation("InitializeStateMachine")]
    public async Task<Outcome> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        StateMachine = Activator.CreateInstance<T>();
        CurrentState = GetCurrentState();
        await ApplyConfigurationAsync(cancellationToken).ConfigureAwait(false);
        RegisterStateChangeHandler();
        return Outcome.Success();
    }

    [Operation("ExecuteTransition")]
    public async Task<Outcome> ExecuteTransitionAsync(
        string transitionName,
        CancellationToken cancellationToken = default)
    {
        if (transitionName is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Transition name was null"), ("InstanceId", _instanceId)]);
            return Outcome.Failure();
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        var admitted = await _coordinator.EnterTransitionAsync(
            Configuration.MaxTransitionsPerMinute,
            cancellationToken).ConfigureAwait(false);

        if (!admitted)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Transition rate limit exceeded"), ("InstanceId", _instanceId), ("MaxTransitionsPerMinute", (object?)Configuration.MaxTransitionsPerMinute ?? "unbounded")]);
            return Outcome.Failure();
        }

        try
        {
            var result = StateMachine.ExecuteTransition(transitionName);

            if (!result.IsSuccess)
            {
                IsHealthy = false;
                Observe(
                    LogLevel.Warning,
                    null,
                    values: [("Reason", "State machine transition failed"), ("InstanceId", _instanceId), ("TransitionName", transitionName)]);
                return Outcome.Failure();
            }

            IsHealthy = true;
            LastTransition = DateTime.UtcNow;
            CurrentState = StateMachine.CurrentState;
            if (_monitor is not null)
            {
                await _monitor.RecordTransitionAsync(this, transitionName, cancellationToken).ConfigureAwait(false);
            }
            return Outcome.Success();
        }
        finally
        {
            _coordinator.ExitTransition();
        }
    }

    [Operation("CreateSnapshot")]
    public async Task<Outcome<StateMachineSnapshot>> CreateSnapshot()
    {
        return Outcome<StateMachineSnapshot>.Success(new StateMachineSnapshot
        {
            InstanceId = InstanceId,
            Type = Type.AssemblyQualifiedName!,
            Version = CURRENT_SNAPSHOT_VERSION,
            CurrentState = CurrentState,
            Configuration = Configuration ?? new StateMachineConfiguration(),
            LastTransition = LastTransition,
            CreatedAt = CreatedAt,
            Data = (StateMachineData)SerializeStateMachineData()
        });
    }

    public IEnumerable<string> GetValidTransitions()
    {
        if (StateMachine is null)
        {
            return Enumerable.Empty<string>();
        }

        return StateMachine.GetValidTransitions();
    }

    public async ValueTask DisposeAsync()
    {
        if (StateMachine is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (StateMachine is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private string GetCurrentState()
    {
        return StateMachine.CurrentState;
    }

    private void RegisterStateChangeHandler()
    {
        StateMachine.RegisterStateChangeHandler(
            (from, to) =>
            {
                _coordinator.RunStateChange(() => ApplyStateChange(from, to));
            });
    }

    private void ApplyStateChange(string from, string to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        CurrentState = to;
        LastTransition = DateTime.UtcNow;
        RecordStateChangeObserved(from, to);
    }

    private void RecordStateChangeObserved(string from, string to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        if (_monitor is null)
        {
            return;
        }

        var recordTask = _monitor.RecordStateChangeAsync(
            this,
            from,
            to);

        recordTask.ContinueWith(
            faulted =>
            {
                Observe(
                    LogLevel.Error,
                    faulted.Exception, values: [("InstanceId", _instanceId), ("FromState", from), ("ToState", to)]);
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private Task ApplyConfigurationAsync(CancellationToken cancellationToken)
    {
        if (Configuration.Properties?.Keys.Any() == true)
        {
            StateMachine.Configure(Configuration.Properties);
        }
        return Task.CompletedTask;
    }

    private Dictionary<string, object> SerializeStateMachineData()
    {
        return StateMachine.GetSnapshotData().ToDictionary();
    }

    public const int CURRENT_SNAPSHOT_VERSION = 1;

    [Operation("RestoreFromSnapshot")]
    public Outcome RestoreFromSnapshot(StateMachineSnapshot snapshot)
    {
        if (snapshot is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Snapshot was null"), ("InstanceId", _instanceId)]);
            return Outcome.Failure();
        }

        if (snapshot.Version > CURRENT_SNAPSHOT_VERSION)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Snapshot version is newer than supported"), ("InstanceId", _instanceId), ("SnapshotVersion", snapshot.Version), ("CurrentVersion", CURRENT_SNAPSHOT_VERSION)]);
            return Outcome.Failure();
        }

        var migrated = StateMachineSnapshotMigrator.Migrate(snapshot, CURRENT_SNAPSHOT_VERSION);
        if (!migrated.IsSuccess)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Snapshot could not be migrated"), ("InstanceId", _instanceId)]);
            return Outcome.Failure();
        }

        var current = migrated.Data!;

        var resolvedSnapshotType = SafeTypeResolver.Resolve(current.Type);
        if (resolvedSnapshotType != typeof(T))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Snapshot type does not match instance type"), ("InstanceId", _instanceId), ("SnapshotType", current.Type), ("InstanceType", typeof(T).AssemblyQualifiedName ?? typeof(T).Name)]);
            return Outcome.Failure();
        }

        if (current.Data is null
            && !string.IsNullOrEmpty(current.CurrentState))
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Snapshot declares state but carries no machine data"), ("InstanceId", _instanceId), ("CurrentState", current.CurrentState)]);
            return Outcome.Failure();
        }

        var restoredConfiguration = current.Configuration ?? new StateMachineConfiguration();
        var configurationValidation = ValidateConfiguration(restoredConfiguration);
        if (!configurationValidation.IsSuccess)
        {
            return configurationValidation;
        }

        var applied = _coordinator.RunGuarded(() =>
        {
            CurrentState = current.CurrentState ?? string.Empty;
            LastTransition = current.LastTransition;
            Configuration = restoredConfiguration;

            if (current.Data is not null)
            {
                StateMachine.RestoreFromSnapshot(current.Data);
                CurrentState = StateMachine.CurrentState;
            }
        });

        if (!applied)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "State machine is transitioning and cannot be restored concurrently"), ("InstanceId", _instanceId)]);
            return Outcome.Failure();
        }

        return Outcome.Success();
    }

    private Outcome ValidateConfiguration(StateMachineConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.MaxTransitionsPerMinute is int maxTransitions
            && maxTransitions <= 0)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Configuration MaxTransitionsPerMinute must be greater than zero"), ("InstanceId", _instanceId), ("MaxTransitionsPerMinute", maxTransitions)]);
            return Outcome.Failure();
        }

        if (configuration.AutoCleanupTimeout is TimeSpan timeout
            && timeout < TimeSpan.Zero)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Configuration AutoCleanupTimeout is negative"), ("InstanceId", _instanceId), ("AutoCleanupTimeout", $"{timeout}")]);
            return Outcome.Failure();
        }

        return Outcome.Success();
    }

    public static async Task<Outcome<StateMachineInstance<T>>> FromSnapshotAsync(
        StateMachineSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var instance = new StateMachineInstance<T>();
        instance.Configure(snapshot.InstanceId);
        instance.Configuration = snapshot.Configuration ?? new StateMachineConfiguration();

        var initializeResult = await instance.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!initializeResult.IsSuccess)
        {
            return Outcome<StateMachineInstance<T>>.Failure();
        }

        var restoreResult = instance.RestoreFromSnapshot(snapshot);
        if (!restoreResult.IsSuccess)
        {
            return Outcome<StateMachineInstance<T>>.Failure();
        }

        return Outcome<StateMachineInstance<T>>.Success(instance);
    }
}
