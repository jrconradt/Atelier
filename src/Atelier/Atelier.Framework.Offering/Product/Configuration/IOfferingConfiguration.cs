namespace Atelier.Framework.Offering.Product.Configuration;

public interface IOfferingConfiguration
{
    IOfferingConfiguration AddOffering(IOffering offering);

    IOfferingConfiguration AddOffering<TOffering>(TOffering offering, Action<TOffering> configure)
        where TOffering : class, IOffering;
}
