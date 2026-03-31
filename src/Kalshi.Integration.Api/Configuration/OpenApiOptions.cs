namespace Kalshi.Integration.Api.Configuration;

/// <summary>
/// Configures generated OpenAPI metadata for the API.
/// </summary>
public sealed class OpenApiOptions
{
    /// <summary>
    /// Gets the configuration section name for OpenAPI settings.
    /// </summary>
    public const string SectionName = "OpenApi";

    /// <summary>
    /// Gets or sets a value indicating whether Swagger endpoints are exposed outside development.
    /// </summary>
    public bool EnableSwaggerInNonDevelopment { get; set; }
}
