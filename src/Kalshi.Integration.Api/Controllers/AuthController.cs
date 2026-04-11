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
/// <remarks>
/// Initializes a new instance of the <see cref="AuthController"/> class.
/// </remarks>
/// <param name="jwtOptions">The JWT settings used to issue development tokens.</param>
/// <param name="environment">The current hosting environment.</param>
[ApiController]
[ApiVersion("1.0")]
[AllowAnonymous]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController(IOptions<JwtOptions> jwtOptions, IWebHostEnvironment environment) : ControllerBase
{
    private static readonly string[] AllowedRoles = ["admin", "trader", "operator", "integration"];

    private readonly JwtOptions _jwtOptions = jwtOptions.Value;
    private readonly IWebHostEnvironment _environment = environment;

    /// <summary>
    /// Issues a short-lived development token when the environment or configuration allows it.
    /// </summary>
    /// <param name="request">The optional role and subject overrides for the generated token.</param>
    /// <returns>A bearer token payload suitable for local API testing.</returns>
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

        string[]? requestedRoles = request?.Roles?.Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        string[] roles = requestedRoles is { Length: > 0 }
            ? requestedRoles
            : ["admin"];

        string[] invalidRoles = roles.Except(AllowedRoles, StringComparer.Ordinal).ToArray();
        if (invalidRoles.Length > 0)
        {
            return Problem(
                title: "Invalid role request",
                detail: $"Unsupported roles: {string.Join(", ", invalidRoles)}. Allowed roles: {string.Join(", ", AllowedRoles)}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string issuer = _jwtOptions.Issuer;
        string audience = _jwtOptions.Audience;
        string signingKey = _jwtOptions.SigningKey ?? JwtOptions.DevelopmentSigningKey;
        int tokenLifetimeMinutes = Math.Max(1, _jwtOptions.TokenLifetimeMinutes);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = now.AddMinutes(tokenLifetimeMinutes);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, string.IsNullOrWhiteSpace(request?.Subject) ? "local-dev-user" : request!.Subject.Trim()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            .. roles.Select(role => new Claim(ClaimTypes.Role, role)),
        ];

        SecurityTokenDescriptor tokenDescriptor = new()
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

        JwtSecurityTokenHandler handler = new();
        SecurityToken token = handler.CreateToken(tokenDescriptor);

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
/// Specifies the optional claims to include in a development token request.
/// </summary>
public sealed record DevTokenRequest(string[]? Roles, string? Subject);

/// <summary>
/// Describes the issued development token and its effective identity settings.
/// </summary>
public sealed record DevTokenResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<string> Roles,
    string Issuer,
    string Audience);
