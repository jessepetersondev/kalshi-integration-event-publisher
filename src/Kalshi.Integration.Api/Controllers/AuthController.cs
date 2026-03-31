using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Asp.Versioning;
using Kalshi.Integration.Api.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kalshi.Integration.Api.Controllers;

/// <summary>
/// Issues development authentication tokens for local and test environments where
/// interactive identity infrastructure is not available.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[AllowAnonymous]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController : ControllerBase
{
    private static readonly string[] AllowedRoles = ["admin", "trader", "operator", "integration"];

    private readonly JwtOptions _jwtOptions;
    private readonly IWebHostEnvironment _environment;

    public AuthController(IOptions<JwtOptions> jwtOptions, IWebHostEnvironment environment)
    {
        _jwtOptions = jwtOptions.Value;
        _environment = environment;
    }

    [HttpPost("dev-token")]
    [ProducesResponseType(typeof(DevTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult CreateDevelopmentToken([FromBody] DevTokenRequest? request = null)
    {
        if (!IsDevelopmentTokenIssuanceEnabled())
        {
            return NotFound();
        }

        var requestedRoles = request?.Roles?.Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var roles = requestedRoles is { Length: > 0 }
            ? requestedRoles
            : ["admin"];

        var invalidRoles = roles.Except(AllowedRoles, StringComparer.Ordinal).ToArray();
        if (invalidRoles.Length > 0)
        {
            return Problem(
                title: "Invalid role request",
                detail: $"Unsupported roles: {string.Join(", ", invalidRoles)}. Allowed roles: {string.Join(", ", AllowedRoles)}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var issuer = _jwtOptions.Issuer;
        var audience = _jwtOptions.Audience;
        var signingKey = _jwtOptions.SigningKey ?? JwtOptions.DevelopmentSigningKey;
        var tokenLifetimeMinutes = Math.Max(1, _jwtOptions.TokenLifetimeMinutes);
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(tokenLifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, string.IsNullOrWhiteSpace(request?.Subject) ? "local-dev-user" : request!.Subject.Trim()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = issuer,
            Audience = audience,
            NotBefore = now.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256Signature),
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);

        return Ok(new DevTokenResponse(
            handler.WriteToken(token),
            "Bearer",
            expiresAt,
            roles,
            issuer,
            audience));
    }

    private bool IsDevelopmentTokenIssuanceEnabled()
    {
        return _environment.IsDevelopment()
            || _environment.IsEnvironment("Testing")
            || _jwtOptions.EnableDevelopmentTokenIssuance;
    }
}
/// <summary>
/// Represents a request payload for dev token.
/// </summary>


public sealed record DevTokenRequest(string[]? Roles, string? Subject);
/// <summary>
/// Represents a response payload for dev token.
/// </summary>


public sealed record DevTokenResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<string> Roles,
    string Issuer,
    string Audience);
