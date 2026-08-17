using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Atelier.Framework.Identity.Configuration;
using Atelier.Framework.Identity.Interfaces;

namespace Microsoft.AspNetCore.Builder;

public static class OidcServerEndpointExtensions
{
    public static IEndpointRouteBuilder MapOidcServerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // 1. Discovery document
        endpoints.MapGet(".well-known/openid-configuration", (IOptions<OidcServerOptions> optionsAccessor) =>
        {
            var options = optionsAccessor.Value;
            var discovery = new Dictionary<string, object>
            {
                ["issuer"] = options.Issuer,
                ["authorization_endpoint"] = $"{options.Issuer}/authorize",
                ["token_endpoint"] = $"{options.Issuer}/token",
                ["jwks_uri"] = $"{options.Issuer}/jwks",
                ["userinfo_endpoint"] = $"{options.Issuer}/userinfo",
                ["response_types_supported"] = new[] { "code", "token", "id_token" },
                ["subject_types_supported"] = new[] { "public" },
                ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
                ["scopes_supported"] = new[] { "openid", "profile", "email", "offline_access", "atelier.boutique.read", "atelier.boutique.write" },
                ["token_endpoint_auth_methods_supported"] = new[] { "client_secret_post", "client_secret_basic" }
            };
            return Results.Ok(discovery);
        });

        // 2. JWKS
        endpoints.MapGet("jwks", (IOidcTokenIssuer issuer) =>
        {
            var jwks = issuer.GetJwksJson();
            if (!jwks.IsSuccess)
            {
                return Results.StatusCode(500);
            }
            return Results.Content(jwks.Data, "application/json");
        });

        // 3. Token Endpoint
        endpoints.MapPost("token", async (
            HttpContext context,
            IOidcTokenIssuer issuer,
            IOptions<OidcServerOptions> optionsAccessor) =>
        {
            var options = optionsAccessor.Value;
            var form = await context.Request.ReadFormAsync().ConfigureAwait(false);
            
            var grantType = form["grant_type"].FirstOrDefault();
            var clientId = form["client_id"].FirstOrDefault();
            var clientSecret = form["client_secret"].FirstOrDefault();
            var username = form["username"].FirstOrDefault();
            var password = form["password"].FirstOrDefault();
            var scope = form["scope"].FirstOrDefault();

            // Basic Auth fallback for client credentials
            if (string.IsNullOrEmpty(clientId) && context.Request.Headers.Authorization.FirstOrDefault() is string authHeader)
            {
                if (authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader.Substring(6).Trim()));
                        var parts = decoded.Split(':', 2);
                        if (parts.Length == 2)
                        {
                            clientId = parts[0];
                            clientSecret = parts[1];
                        }
                    }
                    catch
                    {
                        // Ignore decoding error
                    }
                }
            }

            if (string.IsNullOrEmpty(grantType))
            {
                return Results.BadRequest(new { error = "invalid_request", error_description = "grant_type is required" });
            }

            if (string.IsNullOrEmpty(clientId))
            {
                return Results.BadRequest(new { error = "invalid_client", error_description = "client_id is required" });
            }

            // Validate client credentials
            var client = options.Clients.FirstOrDefault(c => c.ClientId == clientId);
            if (client == null || client.ClientSecret != clientSecret)
            {
                return Results.Json(new { error = "invalid_client", error_description = "Invalid client credentials" }, statusCode: 401);
            }

            string accessToken;
            string? idToken = null;

            if (grantType == "client_credentials")
            {
                var requestedScopes = scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                var allowedScopes = requestedScopes.Intersect(client.Scopes).ToList();
                if (!allowedScopes.Any())
                {
                    allowedScopes = client.Scopes;
                }

                var tokenOutcome = issuer.IssueAccessToken(options.Issuer, clientId, scopes: allowedScopes);
                if (!tokenOutcome.IsSuccess)
                {
                    return Results.StatusCode(500);
                }
                accessToken = tokenOutcome.Data!;
            }
            else if (grantType == "password")
            {
                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    return Results.BadRequest(new { error = "invalid_request", error_description = "username and password are required" });
                }

                var user = options.Users.FirstOrDefault(u => u.Username == username && u.Password == password);
                if (user == null)
                {
                    return Results.BadRequest(new { error = "invalid_grant", error_description = "Invalid username or password" });
                }

                var requestedScopes = scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                var allowedScopes = requestedScopes.Intersect(user.Scopes).ToList();
                if (!allowedScopes.Any())
                {
                    allowedScopes = user.Scopes;
                }

                var accessOutcome = issuer.IssueAccessToken(options.Issuer, clientId, user.UserId, allowedScopes, user.Roles);
                var idOutcome = issuer.IssueIdToken(options.Issuer, clientId, user.UserId, user.Username);

                if (!accessOutcome.IsSuccess || !idOutcome.IsSuccess)
                {
                    return Results.StatusCode(500);
                }

                accessToken = accessOutcome.Data!;
                idToken = idOutcome.Data!;
            }
            else
            {
                return Results.BadRequest(new { error = "unsupported_grant_type" });
            }

            var tokenResponse = new Dictionary<string, object>
            {
                ["access_token"] = accessToken,
                ["token_type"] = "Bearer",
                ["expires_in"] = 3600
            };

            if (idToken is not null)
            {
                tokenResponse["id_token"] = idToken;
            }

            return Results.Ok(tokenResponse);
        });

        // 4. User Info Endpoint
        var userInfoHandler = (HttpContext context, IOptions<OidcServerOptions> optionsAccessor) =>
        {
            var options = optionsAccessor.Value;
            var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
                return Results.Json(new { error = "invalid_token" }, statusCode: 401);
            }

            var token = authHeader.Substring(7).Trim();
            
            try
            {
                var handler = new JwtSecurityTokenHandler();
                if (!handler.CanReadToken(token))
                {
                    return Results.Json(new { error = "invalid_token" }, statusCode: 401);
                }

                var jwt = handler.ReadJwtToken(token);
                var sub = jwt.Subject;

                if (string.IsNullOrEmpty(sub))
                {
                    return Results.Json(new { error = "invalid_token" }, statusCode: 401);
                }

                var user = options.Users.FirstOrDefault(u => u.UserId == sub);
                var userInfo = new Dictionary<string, object>
                {
                    ["sub"] = sub
                };

                if (user != null)
                {
                    userInfo["preferred_username"] = user.Username;
                    userInfo["name"] = user.Username;
                    userInfo["email"] = $"{user.Username}@atelier.com";
                }

                return Results.Ok(userInfo);
            }
            catch
            {
                return Results.Json(new { error = "invalid_token" }, statusCode: 401);
            }
        };

        endpoints.MapGet("userinfo", userInfoHandler);
        endpoints.MapPost("userinfo", userInfoHandler);

        return endpoints;
    }
}
