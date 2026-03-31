namespace Kalshi.Integration.Api.Configuration;

/// <summary>
/// Configures generated OpenAPI metadata for the API.
/// </summary>
public sealed class OpenApiOptions
{
    public const string SectionName = "OpenApi";

    public bool EnableSwaggerInNonDevelopment { get; set; }
}
