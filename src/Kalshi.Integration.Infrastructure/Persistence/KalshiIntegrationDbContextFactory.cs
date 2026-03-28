using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kalshi.Integration.Infrastructure.Persistence;

public sealed class KalshiIntegrationDbContextFactory : IDesignTimeDbContextFactory<KalshiIntegrationDbContext>
{
    public KalshiIntegrationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<KalshiIntegrationDbContext>();
        optionsBuilder.UseSqlite("Data Source=kalshi-integration-sandbox.db");

        return new KalshiIntegrationDbContext(optionsBuilder.Options);
    }
}
