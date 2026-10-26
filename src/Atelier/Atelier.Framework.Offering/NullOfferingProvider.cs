using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Offering;

public sealed class NullOfferingProvider : IOfferingProvider
{
    public TOffering? GetOffering<TOffering>() where TOffering : class => null;

    public Outcome<TOffering> GetRequiredOffering<TOffering>() where TOffering : class
        => Outcome<TOffering>.Failure();

    public IEnumerable<TOffering> GetOfferings<TOffering>() where TOffering : class => Enumerable.Empty<TOffering>();

    public object? GetOffering(Type offeringType) => null;

    public Outcome<object> GetRequiredOffering(Type offeringType)
        => Outcome<object>.Failure();
}
