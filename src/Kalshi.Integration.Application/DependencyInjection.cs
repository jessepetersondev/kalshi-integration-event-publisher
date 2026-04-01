using Kalshi.Integration.Application.Dashboard;
using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Application.Risk;
using Kalshi.Integration.Application.Trading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kalshi.Integration.Application;

/// <summary>
/// Registers application-layer services and options with the dependency injection container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers application-layer services and configuration used by the publisher.
    /// </summary>
    /// <param name="services">The service collection being configured.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RiskOptions>()
            .Bind(configuration.GetSection(RiskOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.MaxOrderSize > 0, "Risk:MaxOrderSize must be greater than zero.")
            .ValidateOnStart();

        services.AddScoped<RiskEvaluator>();
        services.AddScoped<IdempotencyService>();
        services.AddScoped<TradingService>();
        services.AddScoped<TradingQueryService>();
        services.AddScoped<DashboardService>();
        return services;
    }
}
