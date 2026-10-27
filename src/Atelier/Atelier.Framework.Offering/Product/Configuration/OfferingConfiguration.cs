using System.Collections.ObjectModel;

namespace Atelier.Framework.Offering.Product.Configuration;

public class OfferingConfiguration : IOfferingConfiguration
{
    private readonly List<IOffering> _offerings = new();
    private readonly Dictionary<IOffering, List<Action>> _configurators = new(ReferenceEqualityComparer.Instance);

    public IOfferingConfiguration AddOffering(IOffering offering)
    {
        ArgumentNullException.ThrowIfNull(offering);

        if (!_offerings.Contains(offering))
        {
            _offerings.Add(offering);
        }

        return this;
    }

    public IOfferingConfiguration AddOffering<TOffering>(TOffering offering, Action<TOffering> configure)
        where TOffering : class, IOffering
    {
        ArgumentNullException.ThrowIfNull(offering);
        ArgumentNullException.ThrowIfNull(configure);

        AddOffering(offering);

        if (!_configurators.TryGetValue(offering, out var configurators))
        {
            configurators = new List<Action>();
            _configurators[offering] = configurators;
        }

        configurators.Add(() => configure(offering));

        return this;
    }

    public ReadOnlyCollection<IOffering> GetOfferings()
    {
        return _offerings.AsReadOnly();
    }

    public IEnumerable<IOffering> ResolveOfferings()
    {
        foreach (var offering in _offerings)
        {
            if (_configurators.TryGetValue(offering, out var configurators))
            {
                foreach (var configurator in configurators)
                {
                    configurator();
                }
            }
        }

        return _offerings.ToList();
    }
}
