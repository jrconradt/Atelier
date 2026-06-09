using Atelier.Framework.Context;

namespace Atelier.Host.{{ boutiqueName }};

public class DefaultContextAccessor : IContextAccessor
{
    private readonly AsyncLocal<IContext?> _currentContext = new();

    public IContext Current
    {
        get
        {
            if (_currentContext.Value != null)
            {
                return _currentContext.Value;
            }

            var systemContext = global::Atelier.Framework.Context.Context.CreateSystemContext("AmbientOperation");
            _currentContext.Value = systemContext;
            return systemContext;
        }
    }

    public void SetCurrent(IContext context)
    {
        _currentContext.Value = context;
    }
}
