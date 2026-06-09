using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Offering;

public interface IOfferingProvider
{
    public TOffering? GetOffering<TOffering>() where TOffering : class;
    public Outcome<TOffering> GetRequiredOffering<TOffering>() where TOffering : class;
    public IEnumerable<TOffering> GetOfferings<TOffering>() where TOffering : class;
    public object? GetOffering(Type offeringType);
    public Outcome<object> GetRequiredOffering(Type offeringType);
}
