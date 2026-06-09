using Atelier.Framework.Primitives;
using Atelier.Framework.Attributes;
using Atelier.Framework.Offering;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Messaging;

[Infrastructure(InfrastructureLifetime.Scoped)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class HandlerFactory : IHandlerFactory
{
    [Requisite] protected readonly IOfferingProvider _offeringProvider = null!;

    public IRequestHandler<TRequest, TResponse>? GetHandler<TRequest, TResponse>()
        where TRequest : class
        where TResponse : class
    {
        return _offeringProvider.GetOffering<IRequestHandler<TRequest, TResponse>>();
    }

    public object? GetHandler(Type handlerType)
    {
        return _offeringProvider.GetOffering(handlerType);
    }

    public bool HasHandler<TRequest, TResponse>()
        where TRequest : class
        where TResponse : class
    {
        return _offeringProvider.GetOffering<IRequestHandler<TRequest, TResponse>>() != null;
    }
}
