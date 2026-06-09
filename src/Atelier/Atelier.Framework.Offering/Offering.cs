namespace Atelier.Framework.Offering;

public abstract class Offering : IOffering
{
    private readonly IOfferingProvider _offeringProvider;
    private int _running;

    protected Offering(IOfferingProvider offeringProvider)
    {
        _offeringProvider = offeringProvider;
    }

    protected TOffering Resolve<TOffering>() where TOffering : class
    {
        var outcome = _offeringProvider.GetRequiredOffering<TOffering>();
        if (!outcome.IsSuccess || outcome.Data is null)
        {
            throw new InvalidOperationException(
                $"Offering of type {typeof(TOffering).FullName} could not be resolved. Ensure it is registered with the offering container.");
        }
        return outcome.Data;
    }

    protected TOffering? TryResolve<TOffering>() where TOffering : class
    {
        return _offeringProvider.GetOffering<TOffering>();
    }

    protected IEnumerable<TOffering> ResolveAll<TOffering>() where TOffering : class
    {
        return _offeringProvider.GetOfferings<TOffering>();
    }

    protected IEnumerable<TOffering> DiscoverOfferings<TOffering>() where TOffering : class
    {
        return _offeringProvider.GetOfferings<TOffering>();
    }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return;
        }

        OnStart();
    }

    public void Stop()
    {
        if (Interlocked.CompareExchange(ref _running, 0, 1) != 1)
        {
            return;
        }

        OnStop();
    }

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    protected abstract void OnStart();
    protected abstract void OnStop();
}
