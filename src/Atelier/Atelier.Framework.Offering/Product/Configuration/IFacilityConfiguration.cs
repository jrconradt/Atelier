namespace Atelier.Framework.Offering.Product.Configuration;

public interface IFacilityConfiguration
{
    IFacilityBuilder<TService> Expose<TService>(string facilityId)
        where TService : class;

    IFacilityBuilder Expose(Type serviceType, string facilityId);
}

public interface IFacilityBuilder<TService> where TService : class
{
    IFacilityBuilder<TService> WithMetadata(string key, object value);

    IFacilityBuilder<TService> InScope(string scope);
}

public interface IFacilityBuilder
{
    IFacilityBuilder WithMetadata(string key, object value);

    IFacilityBuilder InScope(string scope);
}
