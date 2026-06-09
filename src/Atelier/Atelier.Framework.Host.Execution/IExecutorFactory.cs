namespace Atelier.Framework.Host.Execution;

public interface IExecutorFactory
{
    public IExecutor GetExecutor(OfferingExecutionMode mode);
}
