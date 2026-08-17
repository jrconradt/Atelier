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

    protected IContext Context => AmbientContext.Current
        ?? throw new InvalidOperationException(
            "No context available. Ensure a valid context is available before accessing the Context property.");

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
}
