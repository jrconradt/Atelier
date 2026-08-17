# Identity and Authentication

Atelier's Identity system provides built-in mechanisms for issuing and validating JSON Web Tokens (JWTs), integrating with external OpenID Connect (OIDC) identity providers, and hosting a local OIDC Identity Provider (IdP) server.

All identity features reside within the framework library `Atelier.Framework.Identity`.

---

## JWT Token Issuer

For internal or simple JWT authentication, the framework provides `JwtTokenIssuer` (implementing `IJwtTokenIssuer`), which issues tokens signed with a symmetric key.

### Configuration
Configure JWT authentication settings via `JwtAuthenticationOptions`:

```json
{
  "Jwt": {
    "Issuer": "http://localhost:8080",
    "Audience": "atelier-app",
    "SigningKey": "your-super-secret-256-bit-signing-key-here",
    "TokenLifetime": "01:00:00"
  }
}
```

---

## OIDC Client Integration

The OIDC client service (`OidcTokenService`) allows boutiques to delegate authentication to external OIDC providers (e.g. keycloak, Auth0, or Azure AD).

### Token Validation and Extraction
Use `IOidcTokenService` to validate incoming bearer tokens or extract user info:

```csharp
[Infrastructure(InfrastructureLifetime.Scoped)]
public partial class AuthenticationService : IAuthenticationService
{
    [Requisite] private readonly IOidcTokenService _oidcService = null!;

    public async Task<Outcome<ClaimsPrincipal>> AuthenticateRequestAsync(
        string token,
        CancellationToken cancellationToken)
    {
        return await _oidcService.ValidateTokenAsync(token, providerName: "default", cancellationToken);
    }
}
```

---

## Hosting an OIDC IdP Server

For local development or testing topologies requiring a fully functional OIDC identity provider, `Atelier.Framework.Identity` provides a lightweight, cryptographically secure OIDC Server Offering (`OidcTokenIssuer`).

### 1. Registering the OIDC Server
Register the OIDC server and its configured clients and users in your product's service configuration:

```csharp
public override void ConfigureServices(IServiceCollection services)
{
    // Register OIDC server options and the OidcTokenIssuer offering
    services.AddOidcServer(Configuration);
}
```

Options are bound from the `IdentityService` section:

```json
{
  "IdentityService": {
    "Issuer": "http://localhost:5001",
    "Clients": [
      {
        "ClientId": "my-client",
        "ClientSecret": "my-client-secret",
        "Scopes": ["openid", "profile", "email"]
      }
    ],
    "Users": [
      {
        "UserId": "user-123",
        "Username": "test-user",
        "Password": "test-password",
        "Scopes": ["openid", "profile"],
        "Roles": ["User"]
      }
    ]
  }
}
```

### 2. Mapping the OIDC Endpoints
To serve the OIDC endpoints, map the routes inside your Product's `ConfigureEndpoints` lifecycle method:

```csharp
public override void ConfigureEndpoints(IEndpointRouteBuilder endpoints)
{
    // Exposes:
    //  - GET /.well-known/openid-configuration
    //  - GET /jwks
    //  - POST /token (application/x-www-form-urlencoded)
    //  - GET/POST /userinfo
    endpoints.MapOidcServerEndpoints();
}
```

---

## Network Topology & Security

To secure OIDC services, always home the identity offerings within the `Application` or isolated `Security` zones, and control inbound traffic via a **Domain Gateway**:

```text
  [Web Zone: Web Portal]
           |
           | [Gateway: Domain Gateway] (mTLS)
           v
  [Application Zone: Identity Service]
           | (Endpoints mapped via MapOidcServerEndpoints)
           v
  [OidcTokenIssuer] -> Returns Outcome<string> JWT tokens
```
