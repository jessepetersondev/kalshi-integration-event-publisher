using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Api.Configuration;

/// <summary>
/// Configures JWT issuance and validation for the API.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    /// Gets the configuration section name for JWT settings.
    /// </summary>
    public const string SectionName = "Authentication:Jwt";

    /// <summary>
    /// Gets the default token issuer used by the API.
    /// </summary>
    public const string DefaultIssuer = "kalshi-integration-event-publisher";

    /// <summary>
    /// Gets the default token audience used by the API.
    /// </summary>
    public const string DefaultAudience = "kalshi-integration-event-publisher-clients";

    /// <summary>
    /// Gets the fallback signing key used for local development and test environments.
    /// </summary>
    public const string DevelopmentSigningKey = "kalshi-integration-event-publisher-local-dev-signing-key-please-change";

    /// <summary>
    /// Gets or sets the JWT issuer value expected by the API.
    /// </summary>
    [Required]
    public string Issuer { get; set; } = DefaultIssuer;

    /// <summary>
    /// Gets or sets the JWT audience value expected by the API.
    /// </summary>
    [Required]
    public string Audience { get; set; } = DefaultAudience;

    /// <summary>
    /// Gets or sets the symmetric signing key used to validate bearer tokens.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>
    /// Gets or sets the token lifetime in minutes.
    /// </summary>
    [Range(1, 1440)]
    public int TokenLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// Gets or sets a value indicating whether HTTPS metadata is required for JWT flows.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether development token issuance is enabled.
    /// </summary>
    public bool EnableDevelopmentTokenIssuance { get; set; }
}
