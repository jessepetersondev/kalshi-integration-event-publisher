# Environment configuration

This repo now has a clear configuration strategy for **local**, **development/shared**, and **cloud** environments.

## Goals

- keep the local developer path simple
- make non-local settings predictable
- keep secrets out of source control
- use the same configuration shape across environments

## Configuration precedence

### .NET services

The API and executor worker both follow the standard host precedence order used by `WebApplication.CreateBuilder(args)` or `Host.CreateApplicationBuilder(args)`:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. user secrets in development (if configured)
4. environment variables
5. command-line arguments

That means this repo uses:

- `appsettings.json` for checked-in defaults
- `appsettings.Development.json` for safe local-development defaults
- environment variables or secret stores for anything sensitive

### Node gateway

The Node gateway currently reads configuration from environment variables:

- `PORT`
- `BACKEND_BASE_URL`

Use the checked-in `.env.example` as a copy/paste template, but do not commit real `.env` files.

## Environment strategy

## Local developer machine

Use this mode for the easiest setup.

Recommended shape:

- `ASPNETCORE_ENVIRONMENT=Development`
- SQLite database
- in-memory event publisher
- local JWT dev-token issuance enabled
- local Node gateway URL
- Swagger available

Typical local values:

- `Database__Provider=Sqlite`
- `ConnectionStrings__KalshiIntegration=Data Source=kalshi-integration-event-publisher.dev.db`
- `Authentication__Jwt__EnableDevelopmentTokenIssuance=true`
- `Integrations__NodeGateway__BaseUrl=http://localhost:3001`

Sensitive values should still stay out of committed files. For example:

- JWT signing keys
- SQL Server credentials
- RabbitMQ credentials

For local secret storage, prefer:

- environment variables
- ASP.NET Core user secrets
- an untracked `.env` file for the Node gateway

### Optional local user-secrets example

From `src/Kalshi.Integration.Api`:

```bash
dotnet user-secrets init

dotnet user-secrets set "Authentication:Jwt:SigningKey" "replace-with-a-long-local-secret"
dotnet user-secrets set "ConnectionStrings:KalshiIntegration" "Data Source=kalshi-integration-event-publisher.dev.db"
```

## Shared dev / sandbox environment

Use this mode for a team-shared or hosted non-production environment.

Recommended shape:

- `ASPNETCORE_ENVIRONMENT=Development` or a dedicated non-production environment name
- SQL Server or Azure SQL if you want realistic integration behavior
- optional RabbitMQ publisher
- development token issuance disabled unless the environment is intentionally internal-only
- Swagger enabled only if explicitly desired

Recommended overrides:

- `Database__Provider=SqlServer`
- `ConnectionStrings__KalshiIntegration=<shared non-prod connection string>`
- `Authentication__Jwt__EnableDevelopmentTokenIssuance=false`
- `OpenApi__EnableSwaggerInNonDevelopment=true` only when intentionally exposed
- `EventPublishing__Provider=InMemory` or `RabbitMq`

## Cloud / production-oriented environment

Use environment variables, Azure App Service settings, Azure Container Apps secrets/config, Azure Key Vault, or a comparable external secret/config store.

Recommended shape:

- non-development environment name such as `Production`
- SQL Server / Azure SQL
- production JWT settings from secret/config store
- development token issuance disabled
- Swagger disabled by default
- RabbitMQ only if intentionally provisioned
- readiness checks aligned to real dependencies

Typical cloud overrides:

- `Database__Provider=AzureSql`
- `ConnectionStrings__KalshiIntegration=<azure-sql-connection-string>`
- `Authentication__Jwt__Issuer=<trusted issuer>`
- `Authentication__Jwt__Audience=<trusted audience>`
- `Authentication__Jwt__SigningKey=<secret>`
- `Authentication__Jwt__EnableDevelopmentTokenIssuance=false`
- `OpenApi__EnableSwaggerInNonDevelopment=false`
- `Integrations__NodeGateway__BaseUrl=<gateway-url>`

## Secret-handling rules

Never commit the following to source control:

- production or shared-environment connection strings
- JWT signing keys
- RabbitMQ passwords
- `.env` files with real values
- ad hoc local override files containing secrets

Checked-in files should contain only:

- non-sensitive defaults
- placeholders/examples
- documentation of the configuration shape

## Checked-in config assets in this repo

### .NET API

- `src/Kalshi.Integration.Api/appsettings.json` → baseline defaults
- `src/Kalshi.Integration.Api/appsettings.Development.json` → safe local-development defaults
- `src/Kalshi.Integration.Api/appsettings.Cloud.example.json` → example cloud-oriented shape (template only)

### .NET executor

- `src/Kalshi.Integration.Executor/appsettings.json` → baseline worker defaults
- `src/Kalshi.Integration.Executor/appsettings.Development.json` → safe local-development worker defaults

### Node gateway

- `node-gateway/.env.example` → local template only

## Common environment variables

### .NET API

```bash
export ASPNETCORE_ENVIRONMENT=Development
export Database__Provider=Sqlite
export ConnectionStrings__KalshiIntegration='Data Source=kalshi-integration-event-publisher.dev.db'
export Authentication__Jwt__SigningKey='replace-with-a-long-secret-value'
export Authentication__Jwt__EnableDevelopmentTokenIssuance=true
export Integrations__NodeGateway__BaseUrl='http://localhost:3001'
export OpenTelemetry__OtlpEndpoint='http://localhost:4317'
```

### Node gateway

```bash
export PORT=3001
export BACKEND_BASE_URL='http://localhost:5145'
```

## Azure-oriented notes

For Azure hosting, prefer platform-managed configuration instead of committed files:

- **Azure App Service**: App Settings / Connection Strings
- **Azure Container Apps**: secrets + environment variables
- **Azure Key Vault**: store signing keys, passwords, and connection strings

If the app is deployed to Azure SQL, use:

- `Database__Provider=AzureSql`
- `ConnectionStrings__KalshiIntegration=<azure sql connection string>`

## Practical repo guidance

- use checked-in JSON files for defaults and examples only
- use environment variables or secret stores for anything sensitive
- treat `.env.example` and `appsettings.Cloud.example.json` as templates, not runtime truth
- keep local overrides untracked
