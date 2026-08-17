using Atelier.Framework.Primitives;
using Atelier.Framework.Context;
using Atelier.Framework.Context.Extensions;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Offering.Authorization;

public interface IRequisitionAuthorizer
{
    public bool IsAuthorized(string permission, string resource);
    public bool IsAuthorizedForRole(string role, string resource);
}

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Security))]
public partial class RequisitionAuthorizer : IAtelier, IRequisitionAuthorizer
{

    public bool IsAuthorized(string permission, string resource)
    {
        ArgumentNullException.ThrowIfNull(permission);
        ArgumentNullException.ThrowIfNull(resource);
        var context = AmbientContext.Current;
        var granted = context.IsAuthorized(permission);
        Record("permission", permission, resource, context.GetUserId(), granted);
        return granted;
    }

    public bool IsAuthorizedForRole(string role, string resource)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(resource);
        var context = AmbientContext.Current;
        var granted = context.IsAuthorizedForRole(role);
        Record("role", role, resource, context.GetUserId(), granted);
        return granted;
    }

    private void Record(string kind, string claim, string resource, string? subject, bool granted)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(resource);
        var level = granted ? LogLevel.Information : LogLevel.Warning;
        var decision = granted ? "granted" : "denied";
        Observe(level, values: [("Decision", decision), ("ClaimKind", kind), ("Claim", claim), ("Resource", resource), ("Subject", subject ?? string.Empty)],
        message: $"Authorization {decision} for subject {subject ?? "anonymous"} on {kind} {claim} over resource {resource}");
    }
}
