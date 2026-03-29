# Kalshi Integration Sandbox

Project for Kalshi trade-intent intake, order lifecycle management, webhook delivery, and operational visibility.

## Repository

- GitHub: https://github.com/jessepetersondev/kalshi-integration-sandbox
- Default branch: `master`
- Visibility: public

This repository is published and accessible for portfolio review.

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
- JPC-1555: JWT authentication and policy-based authorization for trading and operational endpoints
- JPC-1556: strongly typed options validation and startup configuration guards
- JPC-1557: outbound HTTP integration hardening with `IHttpClientFactory`, resilience, and correlation propagation

## Authentication and authorization

The API now uses **JWT bearer authentication** with **policy-based authorization** instead of ad hoc endpoint checks.

Current policy intent:
- `trading.write` → write access for `admin` or `trader`
- `trading.read` → read access for `admin`, `trader`, or `operator`
- `operations.read` → operational/dashboard access for `admin` or `operator`
- `integration.write` → inbound integration/update access for `admin` or `integration`

Public endpoints remain intentionally anonymous:
- `/`
- `/dashboard` and static dashboard assets
- `/health/live`
- `/health/ready`
- `/api/v1/system/ping`
- `/api/v1/auth/dev-token` when development-token issuance is enabled

Protected examples:
- `POST /api/v1/trade-intents`
- `POST /api/v1/orders`
- `GET /api/v1/orders/{id}`
- `GET /api/v1/positions`
- `GET /api/v1/dashboard/*`
- `POST /api/v1/integrations/execution-updates`
- `GET /api/v1/system/dependencies/node-gateway`

### Swagger exposure behavior

- **Development / Testing**: Swagger UI is available
- **Non-development**: Swagger stays off by default
- To opt in outside development, set:

```json
{
  "OpenApi": {
    "EnableSwaggerInNonDevelopment": true
  }
}
```

### Local token workflow

For local work, the app can issue a development JWT so you can exercise protected endpoints without wiring a real identity provider first.

Development settings example:

```json
{
  "Authentication": {
    "Jwt": {
      "Issuer": "kalshi-integration-sandbox",
      "Audience": "kalshi-integration-sandbox-clients",
      "SigningKey": "kalshi-integration-sandbox-local-dev-signing-key-please-change",
      "TokenLifetimeMinutes": 60,
      "EnableDevelopmentTokenIssuance": true
    }
  }
}
```

Issue a token locally:

```bash
curl -s http://localhost:5000/api/v1/auth/dev-token \
  -H 'Content-Type: application/json' \
  -d '{"roles":["admin","operator","trader","integration"],"subject":"local-dev-user"}'
```

Use the returned access token:

```bash
TOKEN="<paste access token>"

curl -s http://localhost:5000/api/v1/dashboard/orders \
  -H "Authorization: Bearer $TOKEN"
```

The operator dashboard also includes a local token box plus a **Issue local dev token** shortcut for development/testing environments.

## Startup configuration validation

Critical configuration now binds to **strongly typed options** and fails fast during startup when invalid.

Validated areas currently include:
- `Risk`
- `Database`
- `EventPublishing`
- `RabbitMq`
- `Integrations:NodeGateway`
- `Authentication:Jwt`
- `OpenApi`

Examples of guarded settings:
- `Risk:MaxOrderSize` must be greater than zero
- `Database:Provider` must resolve to `Sqlite`, `SqlServer`, or `AzureSql`
- `ConnectionStrings:KalshiIntegration` is required for the selected provider
- `EventPublishing:Provider` must be `InMemory` or `RabbitMq`
- `RabbitMq:Port` must be a valid TCP port
- `Integrations:NodeGateway:BaseUrl` must be an absolute URL
- `Integrations:NodeGateway:HealthPath` must start with `/`
- `Authentication:Jwt:SigningKey` must be configured and at least 32 characters long

This keeps broken local/app-host configuration from surfacing later as runtime-only failures.

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

## Deployment artifacts

The repo now includes lightweight container/deployment artifacts for local multi-service runs:
- `src/Kalshi.Integration.Api/Dockerfile`
- `node-gateway/Dockerfile`
- `docker-compose.yml`
- `.dockerignore`

Quick start:

```bash
docker compose up --build
```

Local container endpoints:
- API: `http://localhost:5000`
- Gateway: `http://localhost:3001`

Quick verification:

```bash
curl -s http://localhost:5000/health/live
curl -s http://localhost:5000/health/ready
curl -s http://localhost:3001/health
```

Notes:
- the compose stack uses SQLite on a named Docker volume for local/demo runs
- the API container applies migrations on startup
- the gateway talks to the API through the compose network
- these artifacts are meant for local/demo deployment; Azure-oriented hosting can override the database, secrets, and networking model later

See:
- `docs/deployment-artifacts.md`
- `docs/azure-deployment-guide.md`

## Azure deployment guide

The recommended Azure target for this repo is **Azure Container Apps** with:
- one container app for the .NET API
- one container app for the Node gateway
- Azure Container Registry for images
- Azure SQL Database for persistence
- Azure Key Vault for secrets
- Log Analytics / Azure Monitor for platform diagnostics

Why this target fits:
- the repo already has Dockerfiles for both services
- multi-service HTTP deployment is straightforward
- environment-variable and secret-based configuration maps directly to the current app design

See:
- `docs/azure-deployment-guide.md`

## Environment configuration

The repo now has an explicit **local / shared-dev / cloud** configuration story.

Checked-in configuration assets:
- `src/Kalshi.Integration.Api/appsettings.json` → baseline defaults
- `src/Kalshi.Integration.Api/appsettings.Development.json` → safe local-development defaults
- `src/Kalshi.Integration.Api/appsettings.Cloud.example.json` → cloud-oriented template only
- `node-gateway/.env.example` → Node gateway local template only

Configuration strategy:
- keep defaults and examples in source control
- keep secrets out of source control
- use environment variables, ASP.NET Core user secrets, or a platform secret store for sensitive values
- keep local override files untracked

See:
- `docs/environment-configuration.md`

### Secret-handling expectations

Do **not** commit:
- real JWT signing keys
- non-local SQL Server / Azure SQL connection strings
- RabbitMQ passwords
- real `.env` files
- ad hoc local override files with secrets

The `.gitignore` now excludes common local override paths such as:
- `node-gateway/.env`
- `src/Kalshi.Integration.Api/appsettings.Local.json`

### Quick local configuration

API example:

```bash
export ASPNETCORE_ENVIRONMENT=Development
export Authentication__Jwt__SigningKey='replace-with-a-long-secret-value'
export ConnectionStrings__KalshiIntegration='Data Source=kalshi-integration-sandbox.dev.db'
```

Node gateway example:

```bash
export PORT=3001
export BACKEND_BASE_URL='http://localhost:5145'
```

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

## Outbound integrations

The current outbound HTTP dependency path is the **Node gateway** integration.

That path is now wired through `IHttpClientFactory` and hardened with:
- managed `HttpClient` registration instead of ad hoc client construction
- explicit per-attempt timeout configuration
- explicit retry configuration via the standard resilience handler
- correlation-id propagation using `x-correlation-id`
- structured dependency logging aligned with the rest of the app
- optional readiness participation when `Integrations:NodeGateway:IncludeInReadiness=true`

Relevant settings:

```json
{
  "Integrations": {
    "NodeGateway": {
      "Enabled": true,
      "BaseUrl": "http://localhost:3001",
      "HealthPath": "/health",
      "TimeoutSeconds": 5,
      "RetryAttempts": 2,
      "IncludeInReadiness": false
    }
  }
}
```

Operator probe endpoint:
- `GET /api/v1/system/dependencies/node-gateway`

That endpoint is protected by the `operations.read` policy so outbound dependency visibility stays intentional.

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

## Containerized local run

From the repo root:

```bash
docker compose up --build
```

The compose stack publishes:
- API on `http://localhost:5000`
- Node gateway on `http://localhost:3001`

To stop it:

```bash
docker compose down
```
