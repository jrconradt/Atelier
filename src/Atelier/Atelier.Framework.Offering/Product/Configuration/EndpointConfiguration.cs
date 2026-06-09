namespace Atelier.Framework.Offering.Product.Configuration;

public class EndpointConfiguration : IEndpointConfiguration
{
    private readonly List<EndpointDescriptor> _endpoints = new();
    private readonly List<OperationMappingDescriptor> _operationMappings = new();

    public IEndpointConfiguration MapOperations<TService>(string basePath)
        where TService : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

        var mapping = new OperationMappingDescriptor
        {
            ServiceType = typeof(TService),
            BasePath = basePath
        };

        _operationMappings.Add(mapping);

        return this;
    }

    public IEndpointConfiguration MapGet(
        string route,
        Delegate handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(handler);
        return MapEndpoint(HttpMethod.Get, route, handler);
    }

    public IEndpointConfiguration MapPost(
        string route,
        Delegate handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(handler);
        return MapEndpoint(HttpMethod.Post, route, handler);
    }

    public IEndpointConfiguration MapPut(
        string route,
        Delegate handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(handler);
        return MapEndpoint(HttpMethod.Put, route, handler);
    }

    public IEndpointConfiguration MapDelete(
        string route,
        Delegate handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(handler);
        return MapEndpoint(HttpMethod.Delete, route, handler);
    }

    public IEndpointConfiguration MapPatch(
        string route,
        Delegate handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(handler);
        return MapEndpoint(HttpMethod.Patch, route, handler);
    }

    public IEndpointGroup Group(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        return new EndpointGroup(this, prefix);
    }

    public IReadOnlyList<EndpointDescriptor> GetEndpoints()
    {
        return _endpoints.AsReadOnly();
    }

    public IReadOnlyList<OperationMappingDescriptor> GetOperationMappings()
    {
        return _operationMappings.AsReadOnly();
    }

    private IEndpointConfiguration MapEndpoint(
        HttpMethod method,
        string route,
        Delegate handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(handler);

        var descriptor = new EndpointDescriptor
        {
            Method = method,
            Route = route,
            Handler = handler
        };

        _endpoints.Add(descriptor);

        return this;
    }

    internal void AddEndpoint(EndpointDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _endpoints.Add(descriptor);
    }

    private class EndpointGroup : IEndpointGroup
    {
        private readonly EndpointConfiguration _configuration;
        private readonly string _prefix;

        public EndpointGroup(EndpointConfiguration configuration, string prefix)
        {
            _configuration = configuration;
            _prefix = prefix;
        }

        public IEndpointGroup MapGet(string route, Delegate handler)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(route);
            ArgumentNullException.ThrowIfNull(handler);
            return MapEndpoint(HttpMethod.Get, route, handler);
        }

        public IEndpointGroup MapPost(string route, Delegate handler)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(route);
            ArgumentNullException.ThrowIfNull(handler);
            return MapEndpoint(HttpMethod.Post, route, handler);
        }

        public IEndpointGroup MapPut(string route, Delegate handler)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(route);
            ArgumentNullException.ThrowIfNull(handler);
            return MapEndpoint(HttpMethod.Put, route, handler);
        }

        public IEndpointGroup MapDelete(string route, Delegate handler)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(route);
            ArgumentNullException.ThrowIfNull(handler);
            return MapEndpoint(HttpMethod.Delete, route, handler);
        }

        public IEndpointGroup MapPatch(string route, Delegate handler)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(route);
            ArgumentNullException.ThrowIfNull(handler);
            return MapEndpoint(HttpMethod.Patch, route, handler);
        }

        public IEndpointConfiguration EndGroup()
        {
            return _configuration;
        }

        private IEndpointGroup MapEndpoint(
            HttpMethod method,
            string route,
            Delegate handler)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(route);
            ArgumentNullException.ThrowIfNull(handler);

            var fullRoute = $"{_prefix.TrimEnd('/')}/{route.TrimStart('/')}";

            var descriptor = new EndpointDescriptor
            {
                Method = method,
                Route = fullRoute,
                Handler = handler
            };

            _configuration.AddEndpoint(descriptor);

            return this;
        }
    }
}

public class EndpointDescriptor
{
    public required HttpMethod Method { get; set; }
    public required string Route { get; set; }
    public required Delegate Handler { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class OperationMappingDescriptor
{
    public required Type ServiceType { get; set; }
    public required string BasePath { get; set; }
}

public enum HttpMethod
{
    Get,
    Post,
    Put,
    Delete,
    Patch,
    Options,
    Head
}
