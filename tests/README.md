# Testing Conventions

The sandbox now uses three dedicated test projects:

- `Kalshi.Integration.UnitTests`
  - fast isolated tests for domain rules, application services, and small infrastructure/API components exercised without full app hosting
  - uses **xUnit** + **Moq**
  - enforces **85% minimum line coverage** on the unit suite
- `Kalshi.Integration.IntegrationTests`
  - API + persistence flows using `WebApplicationFactory` and EF-backed repository behavior
- `Kalshi.Integration.AcceptanceTests`
  - lightweight end-to-end demo-path coverage that protects the main showcase scenario

## Naming and organization conventions

- project names end with one of:
  - `.UnitTests`
  - `.IntegrationTests`
  - `.AcceptanceTests`
- test classes end with `Tests`
- test methods use `Should...` or behavior-focused `HappyPath...` naming
- keep tests in the lowest practical layer:
  - use **unit** unless a real app host, HTTP surface, or persistence boundary is required
  - use **integration** for repository/API interactions
  - use **acceptance** only for the small number of demo-critical journeys

## Local commands

Run the full suite:

```bash
dotnet test KalshiIntegrationSandbox.sln
```

Run a single layer:

```bash
dotnet test tests/Kalshi.Integration.UnitTests/Kalshi.Integration.UnitTests.csproj
dotnet test tests/Kalshi.Integration.IntegrationTests/Kalshi.Integration.IntegrationTests.csproj
dotnet test tests/Kalshi.Integration.AcceptanceTests/Kalshi.Integration.AcceptanceTests.csproj
```

### Unit coverage gate

The unit test project collects coverage automatically and fails below the configured threshold.
The same Cobertura file is what the Azure DevOps pipeline publishes as unit-test coverage.
Coverage output is written to:

```bash
tests/Kalshi.Integration.UnitTests/TestResults/Coverage/coverage.cobertura.xml
```

## Test setup notes

- API-hosted tests use `WebApplicationFactory<Program>`.
- The app bootstraps the local SQLite database with `Database.EnsureCreated()` at startup.
- Integration and acceptance tests override the connection string to an isolated temporary SQLite file so test/demo data never pollutes the local dashboard database.
- Integration and acceptance tests should prefer unique tickers/correlation ids to avoid cross-test state collisions.
