using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using System.Reflection;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Observability;
using Atelier.Framework.Offering;
using Atelier.Framework.Offering.Product;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Microsoft.Extensions.DependencyInjection;

namespace Atelier.Framework.Attache;

[Infrastructure(InfrastructureLifetime.Transient)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class Boutique : IAtelier, IBoutique
{
    [Requisite] protected readonly IServiceProvider _serviceProvider = null!;
    [Requisite] protected readonly IOfferingProvider _offeringProvider = null!;

    private BoutiqueManifest _manifest = new()
    {
        BoutiqueId = "atelier-unconfigured",
        Name = "atelier-unconfigured"
    };
    private readonly ConcurrentDictionary<string, ProductBase> _products = new();

    private static readonly ConcurrentDictionary<Type, RequisiteField[]> _requisiteFieldCache = new();

    private BoutiqueState _state = BoutiqueState.Created;

    public string BoutiqueId => _manifest.BoutiqueId;
    public string Name => _manifest.Name;
    public BoutiqueState State => _state;
    public BoutiqueManifest Manifest => _manifest;
    public IReadOnlyDictionary<string, ProductBase> Products => _products;

        public Boutique Configure(BoutiqueManifest manifest)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        return this;
    }

    [Operation("Start")]
    public async Task<Outcome> StartAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome.Failure();
        }

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Boutique", BoutiqueId);

        if (_state != BoutiqueState.Created
            && _state != BoutiqueState.Stopped)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Cannot start Boutique from current state"), ("BoutiqueId", BoutiqueId), ("State", _state.ToString())]);
            return Outcome.Failure();
        }

        _state = BoutiqueState.Starting;

        Observe(LogLevel.Information, values: [("BoutiqueId", BoutiqueId), ("Name", Name)]);

        try
        {
            foreach (var productManifest in _manifest.Products)
            {
                if (productManifest.AutoStart)
                {
                    var productResult = await AddProductAsync(
                        productManifest,
                        cancellationToken).ConfigureAwait(false);

                    if (!productResult.IsSuccess)
                    {
                        _state = BoutiqueState.Failed;
                        Observe(
                            LogLevel.Warning,
                            values: [("Reason", "Failed to add product during Boutique start"), ("BoutiqueId", BoutiqueId), ("ProductTypeName", productManifest.ProductTypeName)]);
                        return Outcome.Failure();
                    }
                }
            }

            _state = BoutiqueState.Running;

            Observe(LogLevel.Information, values: [("BoutiqueId", BoutiqueId), ("ProductCount", _products.Count)]);

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            _state = BoutiqueState.Failed;

            Observe(LogLevel.Error, ex, values: [("Reason", "Failed to start Boutique"), ("BoutiqueId", BoutiqueId)]);

            return Outcome.Failure();
        }
    }

    public async Task<Outcome> StopAsync(CancellationToken cancellationToken = default)
    {
        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Boutique", BoutiqueId);

        if (_state != BoutiqueState.Running)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Cannot stop Boutique from current state"), ("BoutiqueId", BoutiqueId), ("State", _state.ToString())]);
            return Outcome.Failure();
        }

        _state = BoutiqueState.Stopping;

        Observe(LogLevel.Information, values: [("BoutiqueId", BoutiqueId)]);

        try
        {
            foreach (var product in _products.Values)
            {
                if (product.IsRunning)
                {
                    await product.StopAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            _state = BoutiqueState.Stopped;

            Observe(LogLevel.Information, values: [("BoutiqueId", BoutiqueId)]);

            return Outcome.Success();
        }
        catch (Exception ex)
        {
            _state = BoutiqueState.Failed;

            Observe(LogLevel.Error, ex, values: [("Reason", "Failed to stop Boutique"), ("BoutiqueId", BoutiqueId)]);

            return Outcome.Failure();
        }
    }

    public async Task<Outcome<string>> AddProductAsync(
        ProductManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Boutique", BoutiqueId);

        Type? productType = manifest.ProductType;

        if (productType == null
            && !string.IsNullOrEmpty(manifest.ProductTypeName))
        {
            if (manifest.ProductTypeName.Contains(',', StringComparison.Ordinal))
            {
                Observe(LogLevel.Warning, values: [("Reason", "Product type name must not be assembly-qualified"), ("BoutiqueId", BoutiqueId), ("ProductTypeName", manifest.ProductTypeName)]);

                return Outcome<string>.Failure();
            }

            productType = SafeTypeResolver.Resolve(manifest.ProductTypeName);
        }

        if (productType == null)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Product type could not be resolved"), ("BoutiqueId", BoutiqueId), ("ProductTypeName", manifest.ProductTypeName)]);
            return Outcome<string>.Failure();
        }

        if (!typeof(ProductBase).IsAssignableFrom(productType))
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Product type must inherit from ProductBase"), ("BoutiqueId", BoutiqueId), ("ProductType", productType.Name)]);
            return Outcome<string>.Failure();
        }

        try
        {
            var product = (ProductBase)ActivatorUtilities.CreateInstance(
                _serviceProvider,
                productType);

            InjectRequisiteFields(product);

            var productId = $"{BoutiqueId}_{product.Name}_{Guid.NewGuid():N}";

            _products[productId] = product;

            Observe(LogLevel.Information, values: [("ProductId", productId), ("ProductType", productType.Name), ("BoutiqueId", BoutiqueId), ("EndpointCount", product.Endpoints.Count), ("FacilityCount", product.FacilityDescriptors.Count)]);

            if (manifest.AutoStart)
            {
                var startResult = await product.StartAsync(cancellationToken).ConfigureAwait(false);
                if (!startResult.IsSuccess)
                {
                    _products.TryRemove(productId, out _);
                    Observe(
                        LogLevel.Warning,
                        values: [("Reason", "Failed to start product"), ("BoutiqueId", BoutiqueId), ("ProductId", productId)]);
                    return Outcome<string>.Failure();
                }

                Observe(LogLevel.Information, values: [("ProductId", productId), ("ProductName", product.Name), ("IsRunning", product.IsRunning)]);
            }

            return Outcome<string>.Success(productId);
        }
        catch (Exception ex)
        {
            Observe(LogLevel.Error, ex, values: [("Reason", "Failed to create product"), ("ProductType", manifest.ProductTypeName), ("BoutiqueId", BoutiqueId)]);

            return Outcome<string>.Failure();
        }
    }

    public async Task<Outcome> RemoveProductAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Product", productId);

        if (!_products.TryGetValue(productId, out var product))
        {
            Observe(
                LogLevel.Information,
                values: [("Message", "Remove of absent product treated as success"), ("ProductId", productId), ("BoutiqueId", BoutiqueId)]);
            return Outcome.Success();
        }

        if (product.IsRunning)
        {
            await product.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        _products.TryRemove(productId, out _);

        Observe(LogLevel.Information, values: [("ProductId", productId), ("BoutiqueId", BoutiqueId)]);

        return Outcome.Success();
    }

    public Task<Outcome<BoutiqueHealthReport>> GetHealthReportAsync(
        CancellationToken cancellationToken = default)
    {
        var issues = new List<string>();

        if (_state != BoutiqueState.Running)
        {
            issues.Add($"Boutique is not running (state: {_state})");
        }

        foreach (var (productId, product) in _products)
        {
            if (!product.IsRunning)
            {
                issues.Add($"Product {productId} is not running");
            }
        }

        var report = new BoutiqueHealthReport
        {
            BoutiqueId = BoutiqueId,
            Name = Name,
            State = _state,
            IsHealthy = _state == BoutiqueState.Running && issues.Count == 0,
            ActiveProducts = _products.Values.Count(p => p.IsRunning),
            TotalOfferings = 0,
            ResourceUsage = new BoutiqueResourceUsage
            {
                MemoryUsageBytes = 0,
                CpuUsagePercent = 0,
                ActiveConnections = 0,
                TotalRequests = 0,
                AverageResponseTimeMs = 0
            },
            Issues = issues
        };

        return Task.FromResult(Outcome<BoutiqueHealthReport>.Success(report));
    }

        private void InjectRequisiteFields(object instance)
    {
        var requisiteFields = _requisiteFieldCache.GetOrAdd(
            instance.GetType(),
            BuildRequisiteFields);

        foreach (var requisiteField in requisiteFields)
        {
            var field = requisiteField.Field;

            var currentValue = field.GetValue(instance);
            if (currentValue != null)
            {
                continue;
            }

            var service = _offeringProvider.GetOffering(field.FieldType);
            if (service != null)
            {
                field.SetValue(instance, service);
            }
            else if (requisiteField.Required)
            {
                throw new InvalidOperationException(
                    $"Cannot resolve required [Requisite] field '{field.Name}' " +
                    $"of type '{field.FieldType.Name}' on '{requisiteField.DeclaringTypeName}'");
            }
        }
    }

        private static RequisiteField[] BuildRequisiteFields(Type concreteType)
    {
        var requisiteFields = new List<RequisiteField>();
        var type = concreteType;

        while (type != null && type != typeof(object))
        {
            var fields = type.GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

            foreach (var field in fields)
            {
                var requisiteAttr = field.GetCustomAttribute<RequisiteAttribute>();
                if (requisiteAttr == null)
                {
                    continue;
                }

                requisiteFields.Add(new RequisiteField(field,
                                                        requisiteAttr.Required,
                                                        type.Name));
            }

            type = type.BaseType;
        }

        return requisiteFields.ToArray();
    }

        private readonly struct RequisiteField
    {
        public RequisiteField(FieldInfo field,
                              bool required,
                              string declaringTypeName)
        {
            Field = field;
            Required = required;
            DeclaringTypeName = declaringTypeName;
        }

        public FieldInfo Field { get; }
        public bool Required { get; }
        public string DeclaringTypeName { get; }
    }
}
