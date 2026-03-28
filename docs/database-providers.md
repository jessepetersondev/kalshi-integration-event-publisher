# Database Providers

JPC-1554 adds a provider switch so the sandbox can run against either:

- **SQLite** for the default local/test workflow
- **SQL Server / Azure SQL** for a stronger Microsoft-stack story

## Supported values

Configuration lives under the `Database` section:

- `Database:Provider=Sqlite` → local/default provider
- `Database:Provider=SqlServer` → SQL Server provider
- `Database:Provider=AzureSql` → accepted alias, normalized to SQL Server
- `Database:ApplyMigrationsOnStartup=true|false`

The active connection string is always:

- `ConnectionStrings:KalshiIntegration`

## Why SQLite stays the default

The clean local development story is still:

- clone the repo
- run the API
- no external database required
- automated tests stay isolated from your local dashboard database

SQLite remains the fastest way to demo the app locally, while SQL Server / Azure SQL is available when you want a more production-like Microsoft deployment shape.

## Local SQLite example

`src/Kalshi.Integration.Api/appsettings.json` defaults to:

```json
{
  "ConnectionStrings": {
    "KalshiIntegration": "Data Source=kalshi-integration-sandbox.db"
  },
  "Database": {
    "Provider": "Sqlite",
    "ApplyMigrationsOnStartup": true
  }
}
```

Run:

```bash
cd src/Kalshi.Integration.Api
dotnet run
```

## Local SQL Server example

Set environment variables before running the API:

```bash
export Database__Provider=SqlServer
export ConnectionStrings__KalshiIntegration='Server=localhost,14333;Initial Catalog=KalshiIntegrationSandbox;User ID=sa;Password=Your_strong_password123;Encrypt=False;TrustServerCertificate=True;Connection Timeout=30;'

cd src/Kalshi.Integration.Api
dotnet run
```

### Optional Docker bootstrap for local SQL Server

```bash
docker run --name kalshi-sql \
  -e ACCEPT_EULA=Y \
  -e MSSQL_SA_PASSWORD='Your_strong_password123' \
  -p 14333:1433 \
  -d mcr.microsoft.com/mssql/server:2022-latest
```

## Azure SQL example

Azure SQL uses the same provider (`SqlServer`) with an Azure-compatible connection string:

```bash
export Database__Provider=SqlServer
export ConnectionStrings__KalshiIntegration='Server=tcp:your-server.database.windows.net,1433;Initial Catalog=KalshiIntegrationSandbox;User ID=your-user;Password=your-password;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
```

Then run the API or EF tooling as usual.

## EF Core tooling

The design-time `KalshiIntegrationDbContextFactory` reads:

- `appsettings.json`
- `appsettings.{Environment}.json`
- environment variables
- command-line overrides

That means `dotnet ef` can target either provider.

### SQLite migrations update

```bash
dotnet tool restore
dotnet ef database update \
  --project src/Kalshi.Integration.Infrastructure \
  --startup-project src/Kalshi.Integration.Api
```

### SQL Server / Azure SQL migrations update

```bash
Database__Provider=SqlServer \
ConnectionStrings__KalshiIntegration='Server=localhost,14333;Initial Catalog=KalshiIntegrationSandbox;User ID=sa;Password=Your_strong_password123;Encrypt=False;TrustServerCertificate=True;Connection Timeout=30;' \
  dotnet ef database update \
  --project src/Kalshi.Integration.Infrastructure \
  --startup-project src/Kalshi.Integration.Api
```

## Operational notes

- SQL Server is configured with `EnableRetryOnFailure()`.
- Readiness checks validate the **configured database provider**, not just SQLite.
- Repository dependency logging now reports the active provider name (`sqlite`, `sqlserver`, or fallback `database`).
- Integration and acceptance tests intentionally stay on isolated temporary SQLite files to keep the local developer workflow simple and repeatable.
