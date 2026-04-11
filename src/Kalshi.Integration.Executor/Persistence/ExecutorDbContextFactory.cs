using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kalshi.Integration.Executor.Persistence;

public sealed class ExecutorDbContextFactory : IDesignTimeDbContextFactory<ExecutorDbContext>
{
    public ExecutorDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<ExecutorDbContext> optionsBuilder = new();
        optionsBuilder.UseSqlite("Data Source=kalshi-integration-executor.db");
        return new ExecutorDbContext(optionsBuilder.Options);
    }
}
