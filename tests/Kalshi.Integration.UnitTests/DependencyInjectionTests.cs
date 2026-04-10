using Kalshi.Integration.Application;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Dashboard;
using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Application.Risk;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Infrastructure;
using Kalshi.Integration.Infrastructure.Integrations.NodeGateway;
using Kalshi.Integration.Infrastructure.Messaging;
using Kalshi.Integration.Infrastructure.Operations;
using Kalshi.Integration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;

namespace Kalshi.Integration.UnitTests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddApplication_ShouldRegisterServicesAndBindRiskOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Mock.Of<ITradeIntentRepository>());
        services.AddSingleton(Mock.Of<IOrderRepository>());
        services.AddSingleton(Mock.Of<IPositionSnapshotRepository>());
        services.AddSingleton(Mock.Of<IOperationalIssueStore>());
        services.AddSingleton(Mock.Of<IAuditRecordStore>());
        services.AddSingleton(Mock.Of<IIdempotencyStore>());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{RiskOptions.SectionName}:MaxOrderSize"] = "7",
                [$"{RiskOptions.SectionName}:RejectDuplicateCorrelationIds"] = "false"
            })
            .Build();

        services.AddApplication(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RiskOptions>>().Value;

        Assert.Equal(7, options.MaxOrderSize);
        Assert.False(options.RejectDuplicateCorrelationIds);
        Assert.NotNull(provider.GetRequiredService<RiskEvaluator>());
        Assert.NotNull(provider.GetRequiredService<IdempotencyService>());
        Assert.NotNull(provider.GetRequiredService<TradingService>());
        Assert.NotNull(provider.GetRequiredService<TradingQueryService>());
        Assert.NotNull(provider.GetRequiredService<DashboardService>());
    }

    [Fact]
    public void AddInfrastructure_ShouldRegisterRepositoryStoresPublisherAndHealthChecks()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var databasePath = Path.Combine(Path.GetTempPath(), $"kalshi-di-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:KalshiIntegration"] = $"Data Source={databasePath}"
            })
            .Build();

        services.AddInfrastructure(configuration);

        using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
        {
            using var scope = provider.CreateScope();

            var concreteRepository = scope.ServiceProvider.GetRequiredService<EfTradingRepository>();
            Assert.Same(concreteRepository, scope.ServiceProvider.GetRequiredService<ITradeIntentRepository>());
            Assert.Same(concreteRepository, scope.ServiceProvider.GetRequiredService<IOrderRepository>());
            Assert.Same(concreteRepository, scope.ServiceProvider.GetRequiredService<IPositionSnapshotRepository>());
            Assert.IsType<EfOperationalIssueStore>(scope.ServiceProvider.GetRequiredService<IOperationalIssueStore>());
            Assert.IsType<EfAuditRecordStore>(scope.ServiceProvider.GetRequiredService<IAuditRecordStore>());
            Assert.IsType<EfIdempotencyStore>(scope.ServiceProvider.GetRequiredService<IIdempotencyStore>());

            var dbContext = scope.ServiceProvider.GetRequiredService<KalshiIntegrationDbContext>();
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<KalshiIntegrationDbContext>>();
            var databaseOptions = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            Assert.NotNull(dbContext);
            Assert.NotNull(dbContextFactory);
            Assert.Contains("Sqlite", dbContext.Database.ProviderName, StringComparison.Ordinal);
            Assert.Equal(DatabaseProviders.Sqlite, databaseOptions.Provider);
            Assert.True(databaseOptions.ApplyMigrationsOnStartup);
            Assert.NotNull(provider.GetRequiredService<HealthCheckService>());

            var publisher = provider.GetRequiredService<IApplicationEventPublisher>();
            var concretePublisher = provider.GetRequiredService<InMemoryApplicationEventPublisher>();
            Assert.Same(concretePublisher, publisher);
        }

        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public void AddInfrastructure_ShouldUseSqlServerProvider_WhenConfigured()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:KalshiIntegration"] = "Server=tcp:kalshi-sql.database.windows.net,1433;Initial Catalog=KalshiIntegrationEventPublisher;User ID=kalshi;Password=StrongPassword!123;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;",
                ["Database:Provider"] = "AzureSql",
                ["Database:ApplyMigrationsOnStartup"] = "false",
            })
            .Build();

        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<KalshiIntegrationDbContext>();
        var databaseOptions = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        Assert.Contains("SqlServer", dbContext.Database.ProviderName, StringComparison.Ordinal);
        Assert.Equal(DatabaseProviders.SqlServer, databaseOptions.Provider);
        Assert.False(databaseOptions.ApplyMigrationsOnStartup);
    }

    [Fact]
    public void AddInfrastructure_ShouldResolveRabbitMqPublisher_WhenConfigured()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var databasePath = Path.Combine(Path.GetTempPath(), $"kalshi-di-rabbit-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:KalshiIntegration"] = $"Data Source={databasePath}",
                [$"{EventPublisherOptions.SectionName}:Provider"] = EventPublisherProviders.RabbitMq,
                [$"{RabbitMqOptions.SectionName}:HostName"] = "localhost",
            })
            .Build();

        services.AddInfrastructure(configuration);

        using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
        {
            var publisher = provider.GetRequiredService<IApplicationEventPublisher>();
            Assert.IsType<RabbitMqApplicationEventPublisher>(publisher);
            Assert.NotNull(provider.GetRequiredService<IConnectionFactory>());
            Assert.NotNull(provider.GetRequiredService<INodeGatewayClient>());
        }

        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public void AddApplication_ShouldRejectInvalidRiskOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Mock.Of<ITradeIntentRepository>());
        services.AddSingleton(Mock.Of<IOrderRepository>());
        services.AddSingleton(Mock.Of<IPositionSnapshotRepository>());
        services.AddSingleton(Mock.Of<IOperationalIssueStore>());
        services.AddSingleton(Mock.Of<IAuditRecordStore>());
        services.AddSingleton(Mock.Of<IIdempotencyStore>());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{RiskOptions.SectionName}:MaxOrderSize"] = "0",
            })
            .Build();

        services.AddApplication(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RiskOptions>>().Value);
    }

    [Fact]
    public void AddInfrastructure_ShouldRejectUnsupportedDatabaseProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:KalshiIntegration"] = "Data Source=:memory:",
                ["Database:Provider"] = "Oracle",
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() => services.AddInfrastructure(configuration));
    }

    [Fact]
    public void AddInfrastructure_ShouldRejectInvalidNodeGatewayBaseUrl()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:KalshiIntegration"] = "Data Source=:memory:",
                ["Integrations:NodeGateway:BaseUrl"] = "not-a-url",
            })
            .Build();

        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<NodeGatewayOptions>>().Value);
    }
}
