# Kalshi Integration Sandbox

Project for Kalshi trade-intent intake, order lifecycle management, webhook delivery, and operational visibility.

## Solution structure

```text
src/
  Kalshi.Integration.Api/
  Kalshi.Integration.Application/
  Kalshi.Integration.Domain/
  Kalshi.Integration.Infrastructure/
  Kalshi.Integration.Contracts/

tests/
  Kalshi.Integration.UnitTests/
  Kalshi.Integration.IntegrationTests/
  Kalshi.Integration.AcceptanceTests/
```

## Architecture

- **Api**: HTTP surface, versioning, Swagger, ProblemDetails, health endpoints
- **Application**: use-case orchestration and service contracts
- **Domain**: core business rules and entities
- **Infrastructure**: persistence, health checks, integrations
- **Contracts**: DTOs and API-facing contracts

Dependency direction follows clean architecture / SOLID principles:
- Domain -> no dependency on outer layers
- Application -> depends on Domain + Contracts
- Infrastructure -> depends on Application + Domain + Contracts
- Api -> depends on Application + Infrastructure + Contracts

## Current completed stories

- JPC-1508: .NET solution and core projects
- JPC-1509: dependency injection, configuration, logging, and health-check foundation
- JPC-1510: API versioning, Swagger, and ProblemDetails error handling
- JPC-1511: Node gateway project structure
- JPC-1512: webhook simulation endpoint
- JPC-1513: Node-to-.NET forwarding client
- JPC-1536: application event publishing abstraction
- JPC-1537: in-memory application event publisher for MVP
- JPC-1538: documented RabbitMQ / Azure Service Bus extension path
- JPC-1539: readiness and liveness endpoints
- JPC-1540: structured request timing and dependency logging
- JPC-1541: dedicated unit/integration/acceptance test project structure
- JPC-1542: integration test coverage for API and persistence flows
- JPC-1543: acceptance tests for end-to-end demo flows
- JPC-1550: Azure DevOps pipeline for build, test, and coverage reporting
- JPC-1551: RabbitMQ publisher adapter behind `IApplicationEventPublisher`
- JPC-1552: stricter .NET build baseline with central package management, analyzers, and format verification
- JPC-1553: checked-in EF Core migrations with startup migration application
- JPC-1554: SQL Server / Azure SQL provider support while keeping SQLite as the clean local default

## Risk validation

The intake flow now includes configurable risk validation for:
- ticker/side/quantity/price checks
- max order size
- duplicate correlation-id rejection
- explicit risk decision output via `POST /api/v1/risk/validate`

Configuration lives under the `Risk` section in appsettings.

## Persistence

The sandbox now supports **EF Core with either SQLite or SQL Server / Azure SQL**.

### Current provider story

- **SQLite** remains the default local-development provider
- **SQL Server / Azure SQL** is supported through provider selection/configuration
- the local/test workflow stays simple because you can still run the app without provisioning external infrastructure

The dashboard itself is **real-data only**: no seeded rows are injected into the UI. Test projects override the connection string to isolated temporary SQLite files so automated tests do not pollute the local operator dashboard database.

Tables covered by the current schema:
- `TradeIntents`
- `Orders`
- `OrderEvents`
- `PositionSnapshots`

Configuration lives in:
- `src/Kalshi.Integration.Api/appsettings.json`
- `src/Kalshi.Integration.Api/appsettings.Development.json`

Key settings:
- `Database:Provider=Sqlite` → default local provider
- `Database:Provider=SqlServer` → SQL Server / Azure SQL provider
- `Database:Provider=AzureSql` → accepted alias for SQL Server provider
- `ConnectionStrings:KalshiIntegration` → active database connection string
- `Database:ApplyMigrationsOnStartup=true` → apply checked-in EF Core migrations at app startup

Schema changes are tracked through checked-in EF Core migrations under:
- `src/Kalshi.Integration.Infrastructure/Persistence/Migrations/`

The design-time `KalshiIntegrationDbContextFactory` now reads appsettings, environment variables, and command-line overrides so `dotnet ef` can target either provider.

For migration / database update work, the repo includes a local tool manifest for `dotnet-ef`:

```bash
dotnet tool restore

dotnet ef database update \
  --project src/Kalshi.Integration.Infrastructure \
  --startup-project src/Kalshi.Integration.Api
```

For SQL Server / Azure SQL, set the provider + connection string first, then run the same EF command.

Examples and connection-string guidance live in:
- `docs/database-providers.md`

## Testing

The sandbox now uses three dedicated test projects:
- `tests/Kalshi.Integration.UnitTests`
- `tests/Kalshi.Integration.IntegrationTests`
- `tests/Kalshi.Integration.AcceptanceTests`

See:
- `tests/README.md`

Repo-wide code quality is enforced through:
- `Directory.Build.props`
- `Directory.Build.targets`
- `Directory.Packages.props`
- `.editorconfig`

Local verification commands:

```bash
dotnet format KalshiIntegrationSandbox.sln --verify-no-changes
dotnet build KalshiIntegrationSandbox.sln
dotnet test KalshiIntegrationSandbox.sln
```

## Azure DevOps CI

The repo now includes an Azure DevOps pipeline at `azure-pipelines.yml`.

The pipeline is designed to:
- restore the .NET solution
- verify formatting with `dotnet format --verify-no-changes`
- build the .NET solution
- run all .NET tests in the solution
- run the Node gateway test suite
- publish .NET test results
- publish the unit-test Cobertura coverage report generated by the xUnit + Moq unit project

The published unit coverage summary comes from:
- `tests/Kalshi.Integration.UnitTests/TestResults/Coverage/coverage.cobertura.xml`

## Event publishing

The sandbox includes a transport-agnostic application event publisher seam with two infrastructure implementations:

- `IApplicationEventPublisher` in the application boundary
- `ApplicationEventEnvelope` as the neutral event contract
- `InMemoryApplicationEventPublisher` for local/default in-process publication and tests
- `RabbitMqApplicationEventPublisher` for broker-backed publishing when enabled via configuration

Current published events include:
- `trade-intent.created`
- `order.created`
- `execution-update.applied`

Provider selection is configuration-driven:
- `EventPublishing:Provider=InMemory` → default local publisher
- `EventPublishing:Provider=RabbitMq` → publishes serialized application events to the configured RabbitMQ exchange with correlation/idempotency metadata mapped into headers

See:
- `docs/event-publishing.md`

## Health and observability

Health endpoints:
- `/health/live` → process liveness
- `/health/ready` → readiness including connectivity to the configured database provider

Verification steps:

```bash
cd src/Kalshi.Integration.Api
dotnet run

curl -s http://localhost:5000/health/live
curl -s http://localhost:5000/health/ready
```

Expected behavior:
- liveness returns **Healthy** with the `self` check
- readiness returns **Healthy** only when the `database` dependency check succeeds
- readiness messages reflect the active provider (for example SQLite vs SQL Server / Azure SQL)

Observability notes:
- request timing is logged for every HTTP request
- database dependency calls are logged with operation names and durations
- provider-aware dependency names are emitted in logs (`sqlite`, `sqlserver`, or `database`)
- request failure logs include method, path, elapsed time, correlation id, and trace id

## Run

Default local run (SQLite):

```bash
cd src/Kalshi.Integration.Api
dotnet run
```

If you want to run against SQL Server / Azure SQL instead, set `Database__Provider=SqlServer` and `ConnectionStrings__KalshiIntegration` first. See:
- `docs/database-providers.md`

Then open:
- `/swagger`
- `/health/live`
- `/health/ready`
- `/api/v1/system/ping`
