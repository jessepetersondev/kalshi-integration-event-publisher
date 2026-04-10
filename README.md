# Kalshi Integration Event Publisher

This repository contains the publisher/API side of the Kalshi integration sandbox:

- an ASP.NET Core / .NET 8 API that owns trade-intent, order, audit, issue, and dashboard state
- a small Node.js gateway used to simulate inbound execution webhooks
- tests and Azure Pipelines validation for the .NET and Node surfaces

This repo does not contain the downstream executor service. When RabbitMQ is enabled, this app can publish command events and consume executor result events, but the executor itself lives outside this repository.

## Repository layout

```text
src/
  Kalshi.Integration.Api/
  Kalshi.Integration.Application/
  Kalshi.Integration.Contracts/
  Kalshi.Integration.Domain/
  Kalshi.Integration.Infrastructure/

tests/
  Kalshi.Integration.UnitTests/
  Kalshi.Integration.IntegrationTests/
  Kalshi.Integration.AcceptanceTests/

node-gateway/
  src/
  tests/

docs/
```

## What the running application does

- Accepts trade intents and applies configured risk checks before persisting them.
- Creates publisher-owned orders from trade intents.
- Publishes order command events through `IApplicationEventPublisher`.
- Accepts execution updates and applies legal order-state transitions.
- Maintains order history, position snapshots, audit records, and operational issues.
- Serves a static operator dashboard backed by `/api/v1/dashboard/*`.
- Exposes a small Kalshi bridge for selected public-market reads and authenticated portfolio/order calls.

## Runtime surface

### Public API endpoints

- `GET /`
- `GET /dashboard`
- `GET /health/live`
- `GET /health/ready`
- `GET /api/v1/system/ping`
- `POST /api/v1/auth/dev-token`
  - available only in `Development`, `Testing`, or when `Authentication__Jwt__EnableDevelopmentTokenIssuance=true`
- `GET /api/v1/kalshi/series`
- `GET /api/v1/kalshi/markets`
- `GET /api/v1/kalshi/markets/{ticker}`

### Protected API endpoints

- `POST /api/v1/trade-intents`
- `POST /api/v1/risk/validate`
- `POST /api/v1/orders`
- `GET /api/v1/orders/{id}`
- `GET /api/v1/orders/outcomes`
- `GET /api/v1/positions`
- `GET /api/v1/dashboard/orders`
- `GET /api/v1/dashboard/positions`
- `GET /api/v1/dashboard/events`
- `GET /api/v1/dashboard/issues`
- `GET /api/v1/dashboard/audit-records`
- `POST /api/v1/integrations/execution-updates`
- `GET /api/v1/system/dependencies/node-gateway`
- `GET /api/v1/kalshi/portfolio/balance`
- `GET /api/v1/kalshi/portfolio/positions`
- `POST /api/v1/kalshi/portfolio/orders`
- `GET /api/v1/kalshi/portfolio/orders/{orderId}`
- `DELETE /api/v1/kalshi/portfolio/orders/{orderId}`

### Node gateway endpoints

- `GET /health`
- `POST /webhooks/simulate/execution-update`

The gateway only validates and forwards simulated execution updates. It does not persist state itself.

### API versioning

The API uses version `v1` in the URL path and also supports `x-api-version`. All checked-in routes currently target v1.

## Local development

### Prerequisites

- .NET SDK 8
- Node.js 22
- Docker, if you want the compose-based stack

### Run the API without RabbitMQ

The default appsettings use in-memory event publishing, but the RabbitMQ result consumer is enabled unless you turn it off. If you are not running a broker locally, disable the consumer explicitly:

```bash
RabbitMq__EnableResultConsumer=false \
dotnet run --project src/Kalshi.Integration.Api
```

Notes:

- `dotnet run` uses the checked-in Development launch profile and listens on `http://localhost:5126`.
- Development config enables Swagger and development-token issuance.
- Development config uses the SQLite file `kalshi-integration-event-publisher.dev.db`.
- Startup applies EF Core migrations automatically outside the `Testing` environment unless `Database__ApplyMigrationsOnStartup=false`.

### Run the Node gateway against the local API

When the API is started via `dotnet run`, point the gateway at port `5126`. The gateway's built-in default backend URL is `http://localhost:5145`, so you should override it in normal local work:

```bash
cd node-gateway
BACKEND_BASE_URL=http://localhost:5126 npm start
```

The gateway listens on `http://localhost:3001`.

### Run with RabbitMQ locally

If you want the broker-backed path locally, start RabbitMQ first and then run the API with broker publishing enabled:

```bash
docker compose up rabbitmq

EventPublishing__Provider=RabbitMq \
dotnet run --project src/Kalshi.Integration.Api
```

The default RabbitMQ connection settings target `localhost:5672` with `guest/guest`.

### Run the full stack with Docker Compose

```bash
docker compose up --build
```

Published endpoints:

- API: `http://localhost:5000`
- Node gateway: `http://localhost:3001`
- RabbitMQ AMQP: `localhost:5672`
- RabbitMQ management UI: `http://localhost:15672`

Important compose behavior:

- The API container runs in `Production`.
- Swagger is off by default.
- Development-token issuance is off by default.
- Compose config switches `EventPublishing__Provider=RabbitMq`.
- Compose config includes the node-gateway readiness dependency in `/health/ready`.

## Authentication and request metadata

### Role/policy mapping

- `trading.write`: `admin`, `trader`
- `trading.read`: `admin`, `trader`, `operator`
- `operations.read`: `admin`, `operator`
- `integration.write`: `admin`, `integration`

### Development tokens

In Development, a local token can be issued with:

```bash
curl -s http://localhost:5126/api/v1/auth/dev-token \
  -H 'Content-Type: application/json' \
  -d '{"roles":["admin","operator","trader","integration"],"subject":"local-dev-user"}'
```

Allowed dev-token roles are:

- `admin`
- `trader`
- `operator`
- `integration`

### Request/response headers

The API normalizes and echoes these headers:

- `x-correlation-id`
- `idempotency-key`
- `x-idempotent-replay`

`trade-intents`, `orders`, and `execution-updates` all support idempotent replay behavior.

## Configuration that matters

### Database

- `Database__Provider`
  - supported values: `Sqlite`, `SqlServer`, `AzureSql`
- `ConnectionStrings__KalshiIntegration`
- `Database__ApplyMigrationsOnStartup`

SQLite is the default local provider. SQL Server / Azure SQL are supported for non-local environments.

### Event publishing and RabbitMQ

- `EventPublishing__Provider`
  - supported values: `InMemory`, `RabbitMq`
- `RabbitMq__EnableResultConsumer`
- `RabbitMq__HostName`
- `RabbitMq__Port`
- `RabbitMq__VirtualHost`
- `RabbitMq__UserName`
- `RabbitMq__Password`
- `RabbitMq__Exchange`
- `RabbitMq__ExchangeType`
- `RabbitMq__RoutingKeyPrefix`
- `RabbitMq__ClientProvidedName`

Two separate RabbitMQ concerns exist in this app:

- outbound command publication through `RabbitMqApplicationEventPublisher`
- inbound executor-result consumption through `RabbitMqResultEventConsumer`

If you are not running a broker, set `RabbitMq__EnableResultConsumer=false` or the API host will fail during startup.

### JWT authentication

- `Authentication__Jwt__Issuer`
- `Authentication__Jwt__Audience`
- `Authentication__Jwt__SigningKey`
- `Authentication__Jwt__TokenLifetimeMinutes`
- `Authentication__Jwt__RequireHttpsMetadata`
- `Authentication__Jwt__EnableDevelopmentTokenIssuance`

Outside Development and Testing, the signing key must be configured and must be at least 32 characters long.

### Node gateway integration

- `Integrations__NodeGateway__Enabled`
- `Integrations__NodeGateway__BaseUrl`
- `Integrations__NodeGateway__HealthPath`
- `Integrations__NodeGateway__TimeoutSeconds`
- `Integrations__NodeGateway__RetryAttempts`
- `Integrations__NodeGateway__IncludeInReadiness`

### Kalshi bridge integration

- `Integrations__KalshiApi__BaseUrl`
- `Integrations__KalshiApi__ApiKeyId`
- `Integrations__KalshiApi__PrivateKeyPath`
- `Integrations__KalshiApi__PrivateKeyPem`
- `Integrations__KalshiApi__Subaccount`
- `Integrations__KalshiApi__TimeoutSeconds`
- `Integrations__KalshiApi__UserAgent`

The public Kalshi bridge GET endpoints do not require Kalshi credentials. The authenticated portfolio/order bridge calls do.

### OpenAPI and observability

- `OpenApi__EnableSwaggerInNonDevelopment`
- `OpenTelemetry__ServiceName`
- `OpenTelemetry__ServiceVersion`
- `OpenTelemetry__OtlpEndpoint`

Swagger is enabled automatically in `Development`. OTLP export is optional and only activates when `OpenTelemetry__OtlpEndpoint` is set to an absolute URI.

## Testing

Run the same checks the pipeline cares about:

```bash
dotnet format KalshiIntegrationEventPublisher.sln --verify-no-changes
dotnet build KalshiIntegrationEventPublisher.sln --configuration Release
dotnet test KalshiIntegrationEventPublisher.sln --configuration Release
cd node-gateway && node --test
```

Test project split:

- `tests/Kalshi.Integration.UnitTests`
- `tests/Kalshi.Integration.IntegrationTests`
- `tests/Kalshi.Integration.AcceptanceTests`

Useful test behavior to know:

- integration and acceptance tests force SQLite temp databases
- integration and acceptance tests force `EventPublishing=InMemory`
- integration and acceptance tests disable `RabbitMq__EnableResultConsumer`

More testing detail lives in [tests/README.md](tests/README.md).

## CI

This repository uses Azure Pipelines, not GitHub Actions. The pipeline definition is [azure-pipelines.yml](azure-pipelines.yml).

Current validation steps:

- restore the solution with NuGet auditing enabled
- verify formatting with `dotnet format --verify-no-changes`
- build in `Release`
- run all .NET tests
- run Node gateway tests
- publish TRX test results
- publish Cobertura coverage artifacts

## Reference docs

- [docs/environment-configuration.md](docs/environment-configuration.md)
- [docs/database-providers.md](docs/database-providers.md)
- [docs/event-publishing.md](docs/event-publishing.md)
- [docs/observability.md](docs/observability.md)
- [docs/deployment-artifacts.md](docs/deployment-artifacts.md)
- [docs/azure-deployment-guide.md](docs/azure-deployment-guide.md)
- [docs/azure-devops-quality-gates.md](docs/azure-devops-quality-gates.md)
- [node-gateway/README.md](node-gateway/README.md)
- [tests/README.md](tests/README.md)
