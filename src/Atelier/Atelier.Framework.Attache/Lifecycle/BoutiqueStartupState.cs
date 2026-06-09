using Atelier.Framework.Primitives;
using System.Runtime.CompilerServices;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Attache.Lifecycle;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public sealed class BoutiqueStartupState
{
    private readonly StrongBox<StoredResult?> _result = new(null);
    private readonly StrongBox<bool> _draining = new(false);

    private sealed class StoredResult
    {
        public StoredResult(Outcome value)
        {
            Value = value;
        }

        public Outcome Value { get; }
    }

    public bool HasStarted => Volatile.Read(ref _result.Value) is not null;

    public bool IsDraining => Volatile.Read(ref _draining.Value);

    public bool IsReady
    {
        get
        {
            if (Volatile.Read(ref _draining.Value))
            {
                return false;
            }

            var current = Volatile.Read(ref _result.Value);
            return current is not null && current.Value.IsSuccess;
        }
    }

    public void BeginDraining()
    {
        Volatile.Write(ref _draining.Value, true);
    }

    public Outcome? Result
    {
        get
        {
            var current = Volatile.Read(ref _result.Value);
            return current is null
                ? null
                : current.Value;
        }
    }

    public void SetResult(Outcome result)
    {
        Volatile.Write(ref _result.Value, new StoredResult(result));
    }
}
