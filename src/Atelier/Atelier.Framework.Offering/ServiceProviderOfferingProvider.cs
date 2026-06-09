using Atelier.Framework.Primitives;
using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;
using Microsoft.Extensions.DependencyInjection;

namespace Atelier.Framework.Offering;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class ServiceProviderOfferingProvider : IOfferingProvider
{
    private readonly IServiceProvider _serviceProvider;

    public ServiceProviderOfferingProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public TOffering? GetOffering<TOffering>() where TOffering : class
    {
        return _serviceProvider.GetService<TOffering>();
    }

    public Outcome<TOffering> GetRequiredOffering<TOffering>() where TOffering : class
    {
        var offering = _serviceProvider.GetService<TOffering>();
        if (offering == null)
        {
            return Outcome<TOffering>.Failure();
        }
        return Outcome<TOffering>.Success(offering);
    }

    public IEnumerable<TOffering> GetOfferings<TOffering>() where TOffering : class
    {
        return _serviceProvider.GetServices<TOffering>();
    }

    public object? GetOffering(Type offeringType)
    {
        ArgumentNullException.ThrowIfNull(offeringType);

        return _serviceProvider.GetService(offeringType);
    }

    public Outcome<object> GetRequiredOffering(Type offeringType)
    {
        if (offeringType is null)
        {
            return Outcome<object>.Failure();
        }

        var offering = _serviceProvider.GetService(offeringType);
        if (offering == null)
        {
            return Outcome<object>.Failure();
        }
        return Outcome<object>.Success(offering);
    }
}
