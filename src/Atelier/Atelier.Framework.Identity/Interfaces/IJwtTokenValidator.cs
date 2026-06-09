using System.Security.Claims;
using Atelier.Framework.Outcomes;
using Microsoft.IdentityModel.Tokens;

namespace Atelier.Framework.Identity.Interfaces;

public interface IJwtTokenValidator
{
    public Outcome<ClaimsPrincipal> Validate(string token);

    public TokenValidationParameters CreateValidationParameters();
}
