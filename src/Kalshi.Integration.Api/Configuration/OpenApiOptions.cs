namespace Kalshi.Integration.Api.Configuration;

public sealed class OpenApiOptions
{
    public const string SectionName = "OpenApi";

    public bool EnableSwaggerInNonDevelopment { get; set; }
}
