using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Offering.Product.Configuration;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;
using Microsoft.Extensions.DependencyInjection;

namespace Atelier.Framework.Offering.Product;

public abstract partial class ProductBase : IAtelier
{
    [Requisite] protected readonly IOfferingProvider OfferingProvider = null!;

    private readonly List<IOffering> _offerings = new();
    private readonly EndpointConfiguration _endpointConfiguration = new();
    private readonly List<FacilityDescriptor> _facilityDescriptors = new();

    private bool _isRunning;
    private int _configurationState;

    protected ProductBase() { }

    public string Name => GetType().Name.Replace("Product", string.Empty);

    public bool IsRunning => _isRunning;

    public IReadOnlyList<EndpointDescriptor> Endpoints
    {
        get
        {
            EnsureConfigured();
            return _endpointConfiguration.GetEndpoints();
        }
    }

    public IReadOnlyList<OperationMappingDescriptor> OperationMappings
    {
        get
        {
            EnsureConfigured();
            return _endpointConfiguration.GetOperationMappings();
        }
    }

    public IReadOnlyList<FacilityDescriptor> FacilityDescriptors
    {
        get
        {
            EnsureConfigured();
            return _facilityDescriptors.AsReadOnly();
        }
    }

    private void EnsureConfigured()
    {
        if (Volatile.Read(ref _configurationState) == 2)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _configurationState, 1, 0) != 0)
        {
            var spin = new SpinWait();
            while (Volatile.Read(ref _configurationState) != 2)
            {
                spin.SpinOnce();
            }

            return;
        }

        var offeringConfig = new OfferingConfiguration(OfferingProvider);
        ConfigureOfferings(offeringConfig);

        var facilityConfig = new FacilityConfiguration();
        ConfigureFacilities(facilityConfig);
        _facilityDescriptors.AddRange(facilityConfig.GetFacilityDescriptors());

        ConfigureEndpoints(_endpointConfiguration);

        foreach (var offering in offeringConfig.ResolveOfferings())
        {
            _offerings.Add(offering);
        }

        Volatile.Write(ref _configurationState, 2);

        Observe(LogLevel.Debug, values: [("ProductName", Name), ("OfferingCount", _offerings.Count), ("EndpointCount", _endpointConfiguration.GetEndpoints().Count), ("FacilityCount", _facilityDescriptors.Count)]);
    }

    public void CollectConfiguration(IServiceCollection services)
    {
        ConfigureServices(services);
        EnsureConfigured();
    }

    public async Task<Outcome> StartAsync(CancellationToken cancellationToken = default)
    {
        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Product", Name);

        if (_isRunning)
        {
            Observe(LogLevel.Warning, values: [("ProductName", Name), ("Reason", "Product is already running")]);
            return Outcome.Failure();
        }

        EnsureConfigured();

        foreach (var offering in _offerings)
        {
            if (!offering.IsRunning)
            {
                offering.Start();

                Observe(LogLevel.Debug, values: [("ProductName", Name), ("OfferingType", offering.GetType().Name)]);
            }
        }

        var result = await OnStartAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _isRunning = true;

            Observe(LogLevel.Information, values: [("ProductName", Name), ("OfferingCount", _offerings.Count)]);
        }
        else
        {
            foreach (var offering in _offerings)
            {
                if (offering.IsRunning)
                {
                    offering.Stop();
                }
            }

            Observe(LogLevel.Error, values: [("ProductName", Name), ("Reason", "Product start failed")]);
        }

        return result;
    }

    public async Task<Outcome> StopAsync(CancellationToken cancellationToken = default)
    {
        using var __entity = global::Atelier.Framework.Context.EntityContext.Enter(ContextAccessor, "Product", Name);

        if (!_isRunning)
        {
            Observe(LogLevel.Warning, values: [("ProductName", Name), ("Reason", "Product is not running")]);
            return Outcome.Failure();
        }

        var result = await OnStopAsync(cancellationToken).ConfigureAwait(false);

        foreach (var offering in _offerings)
        {
            if (offering.IsRunning)
            {
                offering.Stop();

                Observe(LogLevel.Debug, values: [("ProductName", Name), ("OfferingType", offering.GetType().Name)]);
            }
        }

        if (result.IsSuccess)
        {
            _isRunning = false;

            Observe(LogLevel.Information, values: [("ProductName", Name)]);
        }
        else
        {
            Observe(LogLevel.Error, values: [("ProductName", Name), ("Reason", "Product stop failed")]);
        }

        return result;
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
    }

    protected virtual void ConfigureOfferings(IOfferingConfiguration offerings)
    {
        ArgumentNullException.ThrowIfNull(offerings);
    }

    protected virtual void ConfigureFacilities(IFacilityConfiguration facilities)
    {
        ArgumentNullException.ThrowIfNull(facilities);
    }

    protected virtual void ConfigureEndpoints(IEndpointConfiguration endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
    }

    protected abstract Task<Outcome> OnStartAsync(CancellationToken cancellationToken);

    protected abstract Task<Outcome> OnStopAsync(CancellationToken cancellationToken);

    protected void LogProductActivity(
        string activity,
        params ReadOnlySpan<(string Key, object Value)> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activity);

        var logValues = new List<(string Key, object Value)>
        {
            ("ProductName", Name),
            ("Activity", activity),
            ("Timestamp", DateTime.UtcNow)
        };

        foreach (var pair in values)
        {
            logValues.Add(pair);
        }

        Observe(LogLevel.Information, values: logValues.ToArray());
    }

    protected async Task<Outcome<T>> ExecuteProductOperationAsync<T>(
        string operationName,
        Func<Task<Outcome<T>>> operation,
        params (string Key, object Value)[] values)
    {
        var startTime = DateTime.UtcNow;

        LogProductActivity(
            $"{operationName} started",
            values);

        try
        {
            var result = await operation().ConfigureAwait(false);

            var duration = DateTime.UtcNow - startTime;

            if (result.IsSuccess)
            {
                LogProductActivity(
                    $"{operationName} completed",
                    ("Duration", duration.TotalMilliseconds),
                    ("Success", true));
            }
            else
            {
                Observe(LogLevel.Error, values: [("ProductName", Name), ("Operation", operationName), ("Reason", "Product operation failed"), ("Duration", duration.TotalMilliseconds)]);
            }

            return result;
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;

            Observe(LogLevel.Error, ex, values: [("ProductName", Name), ("Operation", operationName), ("Reason", "Product operation threw exception"), ("Duration", duration.TotalMilliseconds)]);

            return Outcome<T>.Failure();
        }
    }

    protected async Task<Outcome> ExecuteProductOperationAsync(
        string operationName,
        Func<Task<Outcome>> operation,
        params (string Key, object Value)[] values)
    {
        var startTime = DateTime.UtcNow;

        LogProductActivity(
            $"{operationName} started",
            values);

        try
        {
            var result = await operation().ConfigureAwait(false);

            var duration = DateTime.UtcNow - startTime;

            if (result.IsSuccess)
            {
                LogProductActivity(
                    $"{operationName} completed",
                    ("Duration", duration.TotalMilliseconds),
                    ("Success", true));
            }
            else
            {
                Observe(LogLevel.Error, values: [("ProductName", Name), ("Operation", operationName), ("Reason", "Product operation failed"), ("Duration", duration.TotalMilliseconds)]);
            }

            return result;
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;

            Observe(LogLevel.Error, ex, values: [("ProductName", Name), ("Operation", operationName), ("Reason", "Product operation threw exception"), ("Duration", duration.TotalMilliseconds)]);

            return Outcome.Failure();
        }
    }
}
