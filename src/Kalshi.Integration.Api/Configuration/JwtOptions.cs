using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Api.Configuration;

/// <summary>
/// Represents configuration for jwt.
/// </summary>


public sealed class JwtOptions
{
    public const string SectionName = "Authentication:Jwt";
    public const string DefaultIssuer = "kalshi-integration-event-publisher";
    public const string DefaultAudience = "kalshi-integration-event-publisher-clients";
    public const string DevelopmentSigningKey = "kalshi-integration-event-publisher-local-dev-signing-key-please-change";

    [Required]
    public string Issuer { get; set; } = DefaultIssuer;

    [Required]
    public string Audience { get; set; } = DefaultAudience;

    public string? SigningKey { get; set; }

    [Range(1, 1440)]
    public int TokenLifetimeMinutes { get; set; } = 60;

    public bool RequireHttpsMetadata { get; set; }

    public bool EnableDevelopmentTokenIssuance { get; set; }
}
