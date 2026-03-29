using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Api.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Authentication:Jwt";
    public const string DefaultIssuer = "kalshi-integration-sandbox";
    public const string DefaultAudience = "kalshi-integration-sandbox-clients";
    public const string DevelopmentSigningKey = "kalshi-integration-sandbox-local-dev-signing-key-please-change";

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
