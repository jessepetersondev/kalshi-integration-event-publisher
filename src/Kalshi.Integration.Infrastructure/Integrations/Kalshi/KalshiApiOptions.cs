using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Infrastructure.Integrations.Kalshi;

/// <summary>
/// Configures direct Kalshi API access used by the publisher bridge.
/// </summary>
public sealed class KalshiApiOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "Integrations:KalshiApi";

    /// <summary>
    /// Gets or sets the Kalshi API base URL.
    /// </summary>
    [Required]
    public string BaseUrl { get; set; } = "https://api.elections.kalshi.com/trade-api/v2";

    /// <summary>
    /// Gets or sets the Kalshi API key id.
    /// </summary>
    public string? ApiKeyId { get; set; }

    /// <summary>
    /// Gets or sets the PEM private key path.
    /// </summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    /// Gets or sets the inline PEM private key.
    /// </summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>
    /// Gets or sets the default subaccount id.
    /// </summary>
    public int Subaccount { get; set; }

    /// <summary>
    /// Gets or sets the request timeout in seconds.
    /// </summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Gets or sets the user-agent header sent to Kalshi.
    /// </summary>
    [Required]
    public string UserAgent { get; set; } = "kalshi-integration-event-publisher/1.0";
}
