using Atelier.Framework.Primitives;
using Atelier.Framework.Attributes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Host.Execution;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class ExecutorFactory : IExecutorFactory
{
    [Requisite] protected readonly InProcessExecutor _inProcessExecutor = null!;
    [Requisite] protected readonly OutOfProcessExecutor _outOfProcessExecutor = null!;
    [Requisite] protected readonly DockerExecutor _dockerExecutor = null!;

    private readonly Lazy<Dictionary<OfferingExecutionMode, IExecutor>> _executorsByMode;

    public ExecutorFactory()
    {
        _executorsByMode = new Lazy<Dictionary<OfferingExecutionMode, IExecutor>>(BuildExecutorMap);
    }

    private Dictionary<OfferingExecutionMode, IExecutor> BuildExecutorMap()
    {
        var executors = new IExecutor[]
        {
            _inProcessExecutor,
            _outOfProcessExecutor,
            _dockerExecutor
        };

        return executors.ToDictionary(executor => executor.ExecutionMode);
    }

    public IExecutor GetExecutor(OfferingExecutionMode mode)
    {
        if (_executorsByMode.Value.TryGetValue(mode, out var executor))
        {
            return executor;
        }

        throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
    }
}
