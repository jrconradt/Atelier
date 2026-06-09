using System.Collections.ObjectModel;

namespace Atelier.Framework.Offering.Product.Configuration;

public class OfferingConfiguration : IOfferingConfiguration
{
    private readonly List<Type> _offeringTypes = new();
    private readonly Dictionary<Type, List<Action<object>>> _configurators = new();
    private readonly IOfferingProvider _offeringProvider;

    public OfferingConfiguration(IOfferingProvider offeringProvider)
    {
        _offeringProvider = offeringProvider ?? throw new ArgumentNullException(nameof(offeringProvider));
    }

    public IOfferingConfiguration AddOffering<TOffering>()
        where TOffering : class, IOffering
    {
        return AddOffering(typeof(TOffering));
    }

    public IOfferingConfiguration AddOffering<TOffering>(Action<TOffering> configure)
        where TOffering : class, IOffering
    {
        ArgumentNullException.ThrowIfNull(configure);

        AddOffering(typeof(TOffering));

        if (!_configurators.ContainsKey(typeof(TOffering)))
        {
            _configurators[typeof(TOffering)] = new List<Action<object>>();
        }

        _configurators[typeof(TOffering)].Add(obj => configure((TOffering)obj));

        return this;
    }

    public IOfferingConfiguration AddOffering(Type offeringType)
    {
        ArgumentNullException.ThrowIfNull(offeringType);

        if (!typeof(IOffering).IsAssignableFrom(offeringType))
        {
            throw new ArgumentException(
                $"Type {offeringType.Name} must implement IOffering",
                nameof(offeringType));
        }

        if (!_offeringTypes.Contains(offeringType))
        {
            _offeringTypes.Add(offeringType);
        }

        return this;
    }

    public ReadOnlyCollection<Type> GetOfferingTypes()
    {
        return _offeringTypes.AsReadOnly();
    }

    public IEnumerable<IOffering> ResolveOfferings()
    {
        var offerings = new List<IOffering>();

        foreach (var offeringType in _offeringTypes)
        {
            var offeringObj = _offeringProvider.GetOffering(offeringType);
            if (offeringObj == null)
            {
                throw new InvalidOperationException(
                    $"Offering of type {offeringType.Name} not found in provider");
            }

            if (offeringObj is not IOffering offering)
            {
                throw new InvalidOperationException(
                    $"Offering {offeringType.Name} does not implement IOffering");
            }

            if (_configurators.TryGetValue(offeringType, out var configurators))
            {
                foreach (var configurator in configurators)
                {
                    configurator(offeringObj);
                }
            }

            offerings.Add(offering);
        }

        return offerings;
    }
}
