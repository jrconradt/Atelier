using Atelier.Framework.Primitives;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Facility;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class FacilitySelector : IAtelier, IFacilitySelector
{
    [Operation("SelectFacilityAsync")]
    public async Task<Outcome<IFacility>> SelectFacilityAsync(
        IRequirement requirement,
        IEnumerable<IFacility> availableFacilities,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<IFacility>.Failure();
        }

        if (requirement is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Requirement was null")]);
            return Outcome<IFacility>.Failure();
        }

        if (availableFacilities is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Available facilities collection was null"), ("RequirementId", requirement.RequirementId)]);
            return Outcome<IFacility>.Failure();
        }


        var facilities = availableFacilities.ToList();
        if (!facilities.Any())
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "No facilities available"), ("RequirementId", requirement.RequirementId)]);
            return Outcome<IFacility>.Failure();
        }

        var scoredFacilities = new List<(IFacility Facility, double Score)>();

        foreach (var facility in facilities)
        {
            var availabilityResult = await facility.CheckResourceAvailabilityAsync(
                requirement.ResourceNeeds,
                cancellationToken).ConfigureAwait(false);

            if (!availabilityResult.IsSuccess || !availabilityResult.Data.IsAvailable)
            {
                continue;
            }

            var score = CalculateFacilityScore(
                facility,
                availabilityResult.Data,
                requirement,
                availableFacilities);

            scoredFacilities.Add((facility, score));
        }

        if (!scoredFacilities.Any())
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "No facilities with sufficient resources"), ("RequirementId", requirement.RequirementId)]);
            return Outcome<IFacility>.Failure();
        }

        var bestFacility = scoredFacilities
            .OrderByDescending(x => x.Score)
            .First()
            .Facility;

        Observe(LogLevel.Information, values: [("FacilityId", bestFacility.FacilityId), ("FacilityName", bestFacility.FacilityName), ("RequirementId", requirement.RequirementId)]);

        return Outcome<IFacility>.Success(bestFacility);
    }

    private const double BASE_SCORE = 100.0;
    private const double PREFERRED_FACILITY_BONUS = 50.0;
    private const double INTERNAL_ONLY_IN_PROCESS_BONUS = 30.0;
    private const double EXTERNAL_PREFERRED_OUT_OF_PROCESS_BONUS = 20.0;
    private const double HIGHEST_PERFORMANCE_IN_PROCESS_BONUS = 40.0;
    private const double LOWEST_LATENCY_IN_PROCESS_BONUS = 35.0;
    private const double MEMORY_HEADROOM_RATIO_WEIGHT = 10.0;
    private const double MEMORY_HEADROOM_BONUS_CAP = 20.0;

    private double CalculateFacilityScore(
        IFacility facility,
        ResourceAvailability availability,
        IRequirement requirement,
        IEnumerable<IFacility> allFacilities)
    {
        double score = BASE_SCORE;

        if (requirement.Preferences.PreferredFacilityId == facility.FacilityId)
        {
            score += PREFERRED_FACILITY_BONUS;
        }

        switch (requirement.Preferences.Mode)
        {
            case FulfillmentMode.InternalOnly:
                if (facility.Type == FacilityType.InProcess)
                {
                    score += INTERNAL_ONLY_IN_PROCESS_BONUS;
                }
                break;

            case FulfillmentMode.ExternalPreferred:
                if (facility.Type != FacilityType.InProcess)
                {
                    score += EXTERNAL_PREFERRED_OUT_OF_PROCESS_BONUS;
                }
                break;

            case FulfillmentMode.HighestPerformance:
                if (facility.Type == FacilityType.InProcess)
                {
                    score += HIGHEST_PERFORMANCE_IN_PROCESS_BONUS;
                }
                break;

            case FulfillmentMode.LowestLatency:
                if (facility.Type == FacilityType.InProcess)
                {
                    score += LOWEST_LATENCY_IN_PROCESS_BONUS;
                }
                break;
        }

        if (availability.Available.MaxMemoryBytes.HasValue &&
            requirement.ResourceNeeds.MaxMemoryBytes.HasValue)
        {
            var ratio = (double)availability.Available.MaxMemoryBytes.Value /
                        requirement.ResourceNeeds.MaxMemoryBytes.Value;
            score += Math.Min(ratio * MEMORY_HEADROOM_RATIO_WEIGHT, MEMORY_HEADROOM_BONUS_CAP);
        }

        return score;
    }

    [Operation("FindFacilitiesByOperationAsync")]
    public async Task<Outcome<IEnumerable<IFacility>>> FindFacilitiesByOperationAsync(
        string operationId,
        IEnumerable<IFacility> availableFacilities,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<IEnumerable<IFacility>>.Failure();
        }

        if (operationId is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Operation ID was null")]);
            return Outcome<IEnumerable<IFacility>>.Failure();
        }

        if (availableFacilities is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Available facilities collection was null"), ("OperationId", operationId)]);
            return Outcome<IEnumerable<IFacility>>.Failure();
        }

        await Task.CompletedTask.ConfigureAwait(false);

        var matchingFacilities = availableFacilities
            .Where(f => f.Capabilities.Operations.Contains(operationId))
            .ToList();

        Observe(LogLevel.Information, values: [("OperationId", operationId), ("MatchCount", matchingFacilities.Count)]);

        return Outcome<IEnumerable<IFacility>>.Success(matchingFacilities);
    }

    [Operation("FindFacilitiesByCapabilityAsync")]
    public async Task<Outcome<IEnumerable<IFacility>>> FindFacilitiesByCapabilityAsync(
        CapabilityQuery query,
        IEnumerable<IFacility> availableFacilities,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<IEnumerable<IFacility>>.Failure();
        }

        if (query is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Capability query was null")]);
            return Outcome<IEnumerable<IFacility>>.Failure();
        }

        if (availableFacilities is null)
        {
            Observe(
                LogLevel.Warning,
                null,
                values: [("Reason", "Available facilities collection was null")]);
            return Outcome<IEnumerable<IFacility>>.Failure();
        }

        await Task.CompletedTask.ConfigureAwait(false);

        var matchingFacilities = availableFacilities.Where(facility =>
        {
            var capabilities = facility.Capabilities;

            if (query.RequiredOperations.Any())
            {
                var hasOperations = query.RequireAll
                    ? query.RequiredOperations.All(op => capabilities.Operations.Contains(op))
                    : query.RequiredOperations.Any(op => capabilities.Operations.Contains(op));

                if (!hasOperations)
                {
                    return false;
                }
            }

            if (query.RequiredScopes.Any())
            {
                var hasScopes = query.RequireAll
                    ? query.RequiredScopes.All(scope => capabilities.Security.RequiredScopes.Contains(scope))
                    : query.RequiredScopes.Any(scope => capabilities.Security.RequiredScopes.Contains(scope));

                if (!hasScopes)
                {
                    return false;
                }
            }

            if (query.RequiredTypes.Any())
            {
                var hasTypes = query.RequireAll
                    ? query.RequiredTypes.All(type => capabilities.CanProvide.Contains(type))
                    : query.RequiredTypes.Any(type => capabilities.CanProvide.Contains(type));

                if (!hasTypes)
                {
                    return false;
                }
            }

            if (query.MetadataFilters.Any())
            {
                var hasMetadata = query.RequireAll
                    ? query.MetadataFilters.All(kvp =>
                        capabilities.Metadata.TryGetValue(kvp.Key, out var value) &&
                        value == kvp.Value)
                    : query.MetadataFilters.Any(kvp =>
                        capabilities.Metadata.TryGetValue(kvp.Key, out var value) &&
                        value == kvp.Value);

                if (!hasMetadata)
                {
                    return false;
                }
            }

            return true;
        }).ToList();

        Observe(LogLevel.Information, values: [("MatchCount", matchingFacilities.Count), ("RequireAll", query.RequireAll)]);

        return Outcome<IEnumerable<IFacility>>.Success(matchingFacilities);
    }
}
