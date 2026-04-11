using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kalshi.Integration.Executor.Persistence;

public sealed class ExecutorDbContextFactory : IDesignTimeDbContextFactory<ExecutorDbContext>
{
    public ExecutorDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ExecutorDbContext>();
        optionsBuilder.UseSqlite("Data Source=kalshi-integration-executor.db");
        return new ExecutorDbContext(optionsBuilder.Options);
    }
}
