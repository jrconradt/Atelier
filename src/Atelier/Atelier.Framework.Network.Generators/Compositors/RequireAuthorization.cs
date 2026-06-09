namespace Atelier.Framework.Network.Generators.Compositors;

internal sealed class RequireAuthorization : AuthorizationGuard
{
    public required string GuardBody { get; init; }
}
