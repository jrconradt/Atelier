using Atelier.Framework.Primitives;
using Atelier.Framework.Attache.Contracts;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Infrastructure.Operation;
using Atelier.Framework.Network;
using Atelier.Framework.Observability;
using Atelier.Framework.Offering;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Attache;

[Infrastructure(InfrastructureLifetime.Scoped)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class AttacheRuntimeOffering : OfferingBase, IAttacheRuntimeOffering
{
    [Requisite] protected readonly AttacheHost _attacheHost = null!;

    protected override void OnStart()
    {
        Observe(values: [("Offering", nameof(AttacheRuntimeOffering)), ("Phase", "Start")]);
    }

    protected override void OnStop()
    {
        Observe(values: [("Offering", nameof(AttacheRuntimeOffering)), ("Phase", "Stop")]);
    }

    [Operation("GetRuntimeStatus")]
    public Task<Outcome<AttacheRuntimeStatusDto>> GetRuntimeStatusAsync(
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(Outcome<AttacheRuntimeStatusDto>.Failure());
        }

        var status = new AttacheRuntimeStatusDto
        {
            State = _attacheHost.State.ToString(),
            BoutiqueCount = 0,
            Configuration = new AttacheConfigurationDto
            {
                MaxBoutiques = _attacheHost.Configuration.ResourceLimits.MaxBoutiques ?? -1,
                AutoStartBoutiques = _attacheHost.Configuration.AutoStartBoutiques
            }
        };

        return Task.FromResult(Outcome<AttacheRuntimeStatusDto>.Success(status));
    }

    [Operation("GetHealth")]
    public async Task<Outcome<AttacheHealthReportDto>> GetHealthAsync(
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Outcome<AttacheHealthReportDto>.Failure();
        }

        var healthReport = await _attacheHost.GetHealthReportAsync(cancellationToken).ConfigureAwait(false);

        if (!healthReport.IsSuccess)
        {
            Observe(
                LogLevel.Warning,
                values: [("Reason", "Health report could not be produced")]);
            return Outcome<AttacheHealthReportDto>.Failure();
        }

        var dto = new AttacheHealthReportDto
        {
            OverallHealth = healthReport.Data!.IsHealthy ? "Healthy" : "Unhealthy",
            Timestamp = healthReport.Data!.Timestamp,
            BoutiqueHealths = healthReport.Data!.Boutiques.Select(bh => new BoutiqueHealthSummaryDto
            {
                BoutiqueName = bh.Name,
                Health = bh.IsHealthy ? "Healthy" : "Unhealthy",
                Message = bh.Issues.Any() ? string.Join(", ", bh.Issues) : "OK"
            }).ToList()
        };

        return Outcome<AttacheHealthReportDto>.Success(dto);
    }
}

public interface IAttacheRuntimeOffering
{
    public Task<Outcome<AttacheRuntimeStatusDto>> GetRuntimeStatusAsync(
        CancellationToken cancellationToken);

    public Task<Outcome<AttacheHealthReportDto>> GetHealthAsync(
        CancellationToken cancellationToken);
}
