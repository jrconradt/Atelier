namespace Atelier.Framework.Offering.Product.Configuration;

public class FacilityConfiguration : IFacilityConfiguration
{
    private readonly List<FacilityDescriptor> _facilities = new();

    public IFacilityBuilder<TService> Expose<TService>(string facilityId)
        where TService : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(facilityId);

        var descriptor = new FacilityDescriptor
        {
            FacilityId = facilityId,
            ServiceType = typeof(TService)
        };

        _facilities.Add(descriptor);

        return new FacilityBuilder<TService>(descriptor);
    }

    public IFacilityBuilder Expose(Type serviceType, string facilityId)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(facilityId);

        var descriptor = new FacilityDescriptor
        {
            FacilityId = facilityId,
            ServiceType = serviceType
        };

        _facilities.Add(descriptor);

        return new FacilityBuilder(descriptor, serviceType);
    }

    public IReadOnlyList<FacilityDescriptor> GetFacilityDescriptors()
    {
        return _facilities.AsReadOnly();
    }

    private class FacilityBuilder : IFacilityBuilder
    {
        protected readonly FacilityDescriptor _descriptor;
        private readonly Type _serviceType;

        public FacilityBuilder(FacilityDescriptor descriptor, Type serviceType)
        {
            _descriptor = descriptor;
            _serviceType = serviceType;
        }

        public IFacilityBuilder InScope(string scope)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
            _descriptor.Scope = scope;
            return this;
        }

        public IFacilityBuilder WithMetadata(string key, object value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
            _descriptor.Metadata[key] = value;
            return this;
        }
    }

    private class FacilityBuilder<TService> : FacilityBuilder, IFacilityBuilder<TService>
        where TService : class
    {
        public FacilityBuilder(FacilityDescriptor descriptor)
            : base(descriptor, typeof(TService))
        {
        }

        public new IFacilityBuilder<TService> InScope(string scope)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
            base.InScope(scope);
            return this;
        }

        public new IFacilityBuilder<TService> WithMetadata(string key, object value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);
            base.WithMetadata(key, value);
            return this;
        }
    }
}

public class FacilityDescriptor
{
    public required string FacilityId { get; set; }
    public required Type ServiceType { get; set; }
    public string? Scope { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
