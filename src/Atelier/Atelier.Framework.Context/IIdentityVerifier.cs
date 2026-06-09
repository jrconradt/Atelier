using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Context;

public interface IIdentityVerifier
{
    Task<Outcome<AuthorizationContext>> VerifyAsync(string token, CancellationToken cancellationToken = default);
}
