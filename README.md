# Kalshi Integration Event Publisher

The project mixes:
- **ASP.NET Core / .NET 8** for the core service boundary
- **Node.js** for an external-facing integration gateway / webhook simulator
- **clean architecture + SOLID boundaries** for long-term maintainability
- **EF Core with SQLite or SQL Server / Azure SQL** for practical local and cloud stories
- **Azure-oriented deployment/CI artifacts** for Microsoft-stack credibility

## Repository

- GitHub: https://github.com/jessepetersondev/kalshi-integration-event-publisher
- Default branch: `master`
- Visibility: public

This repository is published and accessible for portfolio review.

## Why this project exists

This sandbox is designed to demonstrate the kind of engineering work that sits between product features and real operational systems:
- intake and validation of external requests
- traceable order lifecycle handling
- durable persistence and event history
- external integration forwarding
- production-minded health, auth, logging, and deployment patterns


## What the system does

At a high level, the sandbox supports these flows:
- accept a **trade intent**
- apply **risk validation**
- create and query **orders**
- receive **execution updates** through a Node gateway and apply them to the .NET backend
- persist **order events** and **position snapshots**
- expose an **operator dashboard** and operational endpoints

## Main user flows

### 1) Trade intent intake
A client submits a trade intent to the .NET API.

The request is:
- validated for shape and business rules
- checked against configured risk rules
- persisted when accepted
- rejected with problem-details responses when invalid

### 2) Order creation and lifecycle queries
Accepted trade intents can be converted into orders.

The system then supports:
- creating orders
- retrieving individual orders
- viewing current position snapshots
- tracking order status changes over time

### 3) Execution update ingestion
The Node gateway simulates an external callback/integration layer.

It can:
- accept simulated webhook payloads
- validate payload shape
- forward updates to the .NET API
- surface forwarding failures clearly

The .NET API then:
- applies valid status transitions
- records append-only order events
- updates position snapshots
- preserves retry-safe/idempotent handling behavior

### 4) Operator visibility
The app includes operational endpoints and dashboard support for:
- orders
- positions
- integration/dependency visibility
- health and readiness state

## Architecture overview

### Solution structure

```text
src/
  Kalshi.Integration.Api/
  Kalshi.Integration.Application/
  Kalshi.Integration.Domain/
  Kalshi.Integration.Infrastructure/
  Kalshi.Integration.Executor/
  Kalshi.Integration.Contracts/

tests/
  Kalshi.Integration.UnitTests/
  Kalshi.Integration.IntegrationTests/
  Kalshi.Integration.AcceptanceTests/

node-gateway/
  src/
  test/
```

### Layer responsibilities

- **Api**
  - HTTP endpoints
  - versioning
  - auth / authorization
  - Swagger / ProblemDetails
  - health endpoints
  - dashboard/static host behavior

- **Application**
  - use-case orchestration
  - service contracts
  - validation/risk coordination
  - application events

- **Domain**
  - entities and invariants
  - lifecycle rules
  - transition safety
  - business exceptions

- **Infrastructure**
  - EF Core persistence
  - outbound integrations
  - health checks
  - publisher implementations
  - provider-specific wiring

- **Executor**
  - worker/service host bootstrap
  - RabbitMQ consumer shell
  - event routing shell
  - Kalshi execution placeholders
  - result-event publishing shell

- **Contracts**
  - request/response DTOs
  - API-facing contracts
  - cross-boundary schemas

### Dependency direction

Dependency direction follows clean architecture / SOLID principles:
- Domain -> no dependency on outer layers
- Application -> depends on Domain + Contracts
- Infrastructure -> depends on Application + Domain + Contracts
- Api -> depends on Application + Infrastructure + Contracts

### Architecture guardrails

- Keep write orchestration in command services such as `TradingService`; route API reads through query-oriented services such as `TradingQueryService`.
- Prefer aggregate-focused interfaces like `ITradeIntentRepository`, `IOrderRepository`, and `IPositionSnapshotRepository` over expanding a single catch-all repository contract.
- Put domain invariants and state transitions in `src/Kalshi.Integration.Domain`; application services should coordinate dependencies rather than re-implement domain rules.
- Keep controllers thin: request metadata, auth, idempotency, audit, and publishing can stay at the API boundary, but persistence and business decisions belong below it.
- Reuse a shared infrastructure adapter when the storage technology is the same, but only expose the narrower application interfaces each caller actually needs.

## End-to-end flow summary

```text
Client
  -> ASP.NET Core API
    -> validation + risk rules
    -> persistence (trade intents / orders / events / positions)
    -> operational APIs and dashboard

External callback simulator
  -> Node gateway
    -> forwards validated execution updates
    -> ASP.NET Core API applies order-state transitions
```

## Technology stack

### Backend
- .NET 8
- ASP.NET Core
- EF Core
- SQLite for clean local development
- SQL Server / Azure SQL support for cloud-oriented deployment

### Integration gateway
- Node.js 22
- lightweight HTTP service for webhook simulation and forwarding
- native `node --test` test coverage

### Quality / tooling
- xUnit
- Moq
- integration and acceptance tests
- repo-wide analyzers and format enforcement
- Azure DevOps pipeline

### Deployment / cloud story
- Dockerfiles for API and gateway
- docker-compose for local multi-service runs
- Azure Container Apps deployment guidance
- Azure-oriented configuration + secret handling guidance

## Project structure explained

### `src/Kalshi.Integration.Api`
Hosts the HTTP API, auth setup, ProblemDetails, Swagger behavior, readiness/liveness endpoints, and operator-facing surface.

### `src/Kalshi.Integration.Application`
Contains the orchestration layer for trade intents, orders, risk evaluation, command/query services, and application event publishing boundaries.

### `src/Kalshi.Integration.Domain`
Contains the core business model and rules that should remain independent from transport, framework, or storage choices.

### `src/Kalshi.Integration.Infrastructure`
Implements persistence, outbound integration behavior, health checks, migration support, and broker/integration adapters.

### `src/Kalshi.Integration.Executor`
Hosts the worker/service shell for the downstream executor, including configuration bootstrap, logging, DI wiring, and the doc-aligned folders for messaging, routing, Kalshi API execution, persistence, and observability.

### `src/Kalshi.Integration.Contracts`
Contains the DTOs and request/response contracts shared across the service boundary.

### `tests/*`
The repo uses dedicated test projects by type:
- unit tests for fast domain/application coverage
- integration tests for API + persistence behavior
- acceptance tests for the main end-to-end demo path

### `node-gateway/`
Represents the external/customer-facing integration seam and makes webhook-style execution updates testable without a real external system.

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
- JPC-1559: refined command/query and repository boundaries to keep application responsibilities cohesive
- JPC-1560: stronger Azure DevOps PR quality gates with analyzer enforcement and NuGet dependency auditing
- JPC-1563: executor worker project skeleton with configuration, logging, and DI shell

## Local setup

### Prerequisites
- .NET SDK 8
- Node.js 22
- Docker (optional, for containerized local runs)

### Quick start

#### API
```bash
cd src/Kalshi.Integration.Api
dotnet run
```

#### Executor worker
```bash
cd src/Kalshi.Integration.Executor
dotnet run
```

#### Node gateway
```bash
cd node-gateway
node src/server.js
```

The default local story uses SQLite.

If you want SQL Server / Azure SQL instead, configure:
- `Database__Provider=SqlServer` or `Database__Provider=AzureSql`
- `ConnectionStrings__KalshiIntegration`

See:
- `docs/database-providers.md`
- `docs/environment-configuration.md`

## Containerized local run

From the repo root:

```bash
docker compose up --build
```

Published endpoints:
- API: `http://localhost:5000`
- Gateway: `http://localhost:3001`

Stop the stack:

```bash
docker compose down
```

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

### Local token workflow

For local work, the app can issue a development JWT so you can exercise protected endpoints without wiring a real identity provider first.

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

## Health and observability

Health endpoints:
- `/health/live` → process liveness
- `/health/ready` → readiness including configured dependencies

Current observability includes:
- structured request timing logs
- database/dependency call logging
- correlation-id and trace-id flow
- provider-aware dependency naming in logs
- OpenTelemetry traces for inbound requests, outbound `HttpClient`, and EF Core database work
- OpenTelemetry metrics for ASP.NET Core, runtime/process activity, and custom request/dependency duration histograms

OpenTelemetry is configurable through the `OpenTelemetry` config section, including an OTLP export endpoint for collector/vendor integration.

See also:
- `docs/environment-configuration.md`
- `docs/deployment-artifacts.md`
- `docs/azure-deployment-guide.md`
- `docs/observability.md`

## Event publishing

The sandbox includes a transport-agnostic application event publishing seam with:
- `IApplicationEventPublisher`
- `InMemoryApplicationEventPublisher`
- `RabbitMqApplicationEventPublisher`

The downstream executor now has a dedicated worker shell under `src/Kalshi.Integration.Executor` so the consume/route/execute/publish path can be implemented in follow-on stories without overloading the API project.

Current published events include:
- `trade-intent.created`
- `order.created`
- `execution-update.applied`

See:
- `docs/event-publishing.md`

## Testing

The repo uses three dedicated test projects:
- `tests/Kalshi.Integration.UnitTests`
- `tests/Kalshi.Integration.IntegrationTests`
- `tests/Kalshi.Integration.AcceptanceTests`

Local verification commands:

```bash
dotnet format KalshiIntegrationEventPublisher.sln --verify-no-changes
dotnet build KalshiIntegrationEventPublisher.sln
dotnet test KalshiIntegrationEventPublisher.sln
```

Node gateway tests:

```bash
cd node-gateway
node --test
```

See:
- `tests/README.md`

## Azure DevOps CI

The repo includes an Azure DevOps pipeline at `azure-pipelines.yml`.

The pipeline is intended to be used as required build validation for PRs targeting `main` and `master`.

The pipeline currently:
- restores the .NET solution with NuGet vulnerability auditing enabled
- verifies formatting with `dotnet format --verify-no-changes`
- builds the solution with analyzers and code-style warnings enforced as errors
- runs .NET tests
- runs Node gateway tests
- publishes .NET test results
- publishes Cobertura unit-test coverage artifacts

See:
- `docs/azure-devops-quality-gates.md`

## Deployment and cloud readiness

The repo now includes:
- environment configuration guidance for local/dev/cloud
- Dockerfiles for API and gateway
- local multi-service `docker-compose.yml`
- Azure Container Apps deployment guidance

See:
- `docs/environment-configuration.md`
- `docs/deployment-artifacts.md`
- `docs/azure-deployment-guide.md`

## Interview and demo narrative

A dedicated walkthrough guide is available at:
- `docs/interview-demo-narrative.md`

It includes:
- a concise project pitch
- how the repo maps to customer integrations and operational workflow work
- a suggested live demo sequence
- reliability and Microsoft-stack talking points
- future enhancements to mention in interviews

## Notes for reviewers

If you are reviewing this as a portfolio project, the important signals are:
- clear service boundaries instead of framework-driven sprawl
- production-minded validation, auth, persistence, and health design
- realistic integration seams through the Node gateway
- test coverage across unit, integration, and acceptance layers
- credible Microsoft-stack deployment and CI stories without overcomplicating the repo
