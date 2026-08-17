using System;
using System.Collections.Generic;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Identity.Interfaces;

public interface IOidcTokenIssuer
{
    Outcome<string> GetJwksJson();

    Outcome<string> IssueAccessToken(
        string issuer,
        string clientId,
        string? subject = null,
        IEnumerable<string>? scopes = null,
        IEnumerable<string>? roles = null,
        TimeSpan? lifetime = null);

    Outcome<string> IssueIdToken(
        string issuer,
        string clientId,
        string subject,
        string username,
        TimeSpan? lifetime = null);
}
