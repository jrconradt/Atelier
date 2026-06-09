namespace Atelier.Framework.Offering.Product.Configuration;

public interface IOfferingConfiguration
{
    IOfferingConfiguration AddOffering<TOffering>()
        where TOffering : class, IOffering;

    IOfferingConfiguration AddOffering<TOffering>(Action<TOffering> configure)
        where TOffering : class, IOffering;

    IOfferingConfiguration AddOffering(Type offeringType);
}
