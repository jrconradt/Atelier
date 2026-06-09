using System.Reflection;
using Atelier.Framework.Context;
using Atelier.Framework.Context.Extensions;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Offering;

public abstract partial class OfferingBase : IAtelier, IOffering
{
    [Requisite] protected readonly IContextAccessor ContextAccessor = null!;

    protected IContext Context => ContextAccessor.Current
        ?? throw new InvalidOperationException(
            "No context available. Ensure a valid context is available before accessing the Context property.");

    [Requisite] public IOfferingProvider OfferingProvider = null!;

    private int _running;

    protected OfferingBase() { }

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

    protected TOffering GetRequiredOffering<TOffering>() where TOffering : class
    {
        var offering = OfferingProvider.GetOffering<TOffering>();
        if (offering == null)
        {
            throw new InvalidOperationException($"Required offering {typeof(TOffering).Name} not found in offering provider");
        }
        return offering;
    }

    protected TOffering? GetOffering<TOffering>() where TOffering : class
    {
        return OfferingProvider.GetOffering<TOffering>();
    }

    protected IEnumerable<TOffering> GetOfferings<TOffering>() where TOffering : class
    {
        return OfferingProvider.GetOfferings<TOffering>();
    }

    protected void ValidateDependencies()
    {
        var requiredOfferings = GetRequiredOfferings();
        foreach (var offeringType in requiredOfferings)
        {
            var offering = OfferingProvider.GetOffering(offeringType);
            if (offering == null)
            {
                throw new InvalidOperationException(
                    $"Required offering {offeringType.Name} not found in offering provider");
            }
        }
    }

    protected virtual Type[] GetRequiredOfferings()
    {
        return Array.Empty<Type>();
    }
}
