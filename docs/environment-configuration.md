# Environment Configuration

The API, executor, and Node gateway share a single configuration strategy:

- checked-in `appsettings*.json` files provide safe defaults
- environment variables and secret stores provide environment-specific overrides
- RabbitMQ safety defaults are enabled in both .NET services

## Configuration precedence

The .NET services follow the standard ASP.NET Core host precedence order:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. user secrets in development when configured
4. environment variables
5. command-line arguments

The Node gateway uses environment variables from `node-gateway/.env.example`.

## Local development

Recommended defaults:

- `ASPNETCORE_ENVIRONMENT=Development`
- SQLite for both publisher and executor
- `EventPublishing__Provider=InMemory` for the API unless RabbitMQ is under test
- `Authentication__Jwt__EnableDevelopmentTokenIssuance=true`
- local Node gateway URL

Typical API variables:

```bash
export ASPNETCORE_ENVIRONMENT=Development
export Database__Provider=Sqlite
export ConnectionStrings__KalshiIntegration='Data Source=kalshi-integration-event-publisher.dev.db'
export EventPublishing__Provider=InMemory
export Authentication__Jwt__SigningKey='replace-with-a-long-secret-value'
export Authentication__Jwt__EnableDevelopmentTokenIssuance=true
export Integrations__NodeGateway__BaseUrl='http://localhost:3001'
export OpenTelemetry__OtlpEndpoint='http://localhost:4317'
```

Typical executor variables:

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Executor='Data Source=kalshi-integration-executor.dev.db'
export RabbitMq__HostName='localhost'
export RabbitMq__UserName='guest'
export RabbitMq__Password='guest'
export OpenTelemetry__OtlpEndpoint='http://localhost:4317'
```

## Shared or cloud environments

Recommended shape:

- SQL Server or Azure SQL for the publisher API
- RabbitMQ enabled for both services
- development token issuance disabled
- OTLP export configured
- readiness checks wired to real dependencies

Typical API overrides:

```bash
export Database__Provider=AzureSql
export ConnectionStrings__KalshiIntegration='<publisher-connection-string>'
export EventPublishing__Provider=RabbitMq
export RabbitMq__HostName='rabbitmq.internal'
export RabbitMq__UserName='kalshi-integration'
export RabbitMq__Password='<secret>'
export Authentication__Jwt__EnableDevelopmentTokenIssuance=false
```

Typical executor overrides:

```bash
export ConnectionStrings__Executor='<executor-connection-string>'
export RabbitMq__HostName='rabbitmq.internal'
export RabbitMq__UserName='kalshi-integration'
export RabbitMq__Password='<secret>'
export Integrations__KalshiApi__BaseUrl='https://api.elections.kalshi.com/trade-api/v2'
```

## RabbitMQ reliability settings

Both services honor the same `RabbitMq` section. Important settings:

- `Mandatory`
- `PublishConfirmTimeoutMilliseconds`
- `PublishRetryAttempts`
- `PublishRetryDelayMilliseconds`
- `OutboxBatchSize`
- `OutboxPollingIntervalMilliseconds`
- `OutboxLeaseDurationSeconds`
- `OutboxMaxAttempts`
- `OutboxInitialRetryDelayMilliseconds`
- `OutboxMaxRetryDelayMilliseconds`
- `OutboxJitterMaxMilliseconds`
- `RepairBatchSize`
- `RepairGraceSeconds`
- `QueueMonitoringIntervalMilliseconds`
- `OutboxDegradedAgeSeconds`
- `OutboxUnhealthyAgeSeconds`
- `CriticalQueueBacklogDegradedAgeSeconds`
- `CriticalQueueBacklogUnhealthyAgeSeconds`
- `EnableResultConsumer`
- `ConsumerRecoveryDelaySeconds`

Queue names and bindings are also configurable:

- `PublisherResultsQueue`
- `PublisherResultsDeadLetterQueue`
- `ExecutorQueue`
- `ExecutorDeadLetterQueue`
- `ExecutorRoutingKeyBinding`
- `ResultsRoutingKeyBinding`

## Checked-in config files

API:

- `src/Kalshi.Integration.Api/appsettings.json`
- `src/Kalshi.Integration.Api/appsettings.Development.json`
- `src/Kalshi.Integration.Api/appsettings.Cloud.example.json`

Executor:

- `src/Kalshi.Integration.Executor/appsettings.json`
- `src/Kalshi.Integration.Executor/appsettings.Development.json`

Gateway:

- `node-gateway/.env.example`

## Secrets

Do not commit:

- database connection strings for shared environments
- JWT signing keys
- RabbitMQ passwords
- Kalshi credentials or tokens
- OTLP collector credentials
