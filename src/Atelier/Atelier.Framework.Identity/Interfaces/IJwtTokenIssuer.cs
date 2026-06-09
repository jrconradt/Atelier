using System.Security.Claims;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Identity.Interfaces;

public interface IJwtTokenIssuer
{
    public Outcome<string> Issue(
        string subject,
        IEnumerable<Claim>? claims = null,
        TimeSpan? lifetime = null);
}
