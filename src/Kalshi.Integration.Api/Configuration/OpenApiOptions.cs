namespace Kalshi.Integration.Api.Configuration;

/// <summary>
/// Represents configuration for open api.
/// </summary>


public sealed class OpenApiOptions
{
    public const string SectionName = "OpenApi";

    public bool EnableSwaggerInNonDevelopment { get; set; }
}
