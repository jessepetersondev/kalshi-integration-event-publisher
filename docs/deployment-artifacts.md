# Deployment artifacts

This story adds lightweight deployment/container artifacts so the sandbox can run in a more production-shaped way without changing the application architecture.

## Added artifacts

- `src/Kalshi.Integration.Api/Dockerfile`
- `node-gateway/Dockerfile`
- `docker-compose.yml`
- `.dockerignore`

## Container shape

### API container

- built from the .NET 8 SDK image and published into the .NET 8 ASP.NET runtime image
- listens on port `8080`
- applies EF Core migrations on startup when `Database__ApplyMigrationsOnStartup=true`
- defaults to a SQLite database file mounted under `/data`

### Gateway container

- built from `node:22-alpine`
- listens on port `3001`
- forwards to the API through the internal compose network using `http://api:8080`

## Local compose workflow

From the repo root:

```bash
docker compose up --build
```

Then verify:

```bash
curl -s http://localhost:5000/health/live
curl -s http://localhost:5000/health/ready
curl -s http://localhost:3001/health
```

Stop the stack:

```bash
docker compose down
```

If you want to remove the named SQLite volume too:

```bash
docker compose down -v
```

## Deployment assumptions

These artifacts are intentionally simple and local-friendly.

Current assumptions:

- local container runs use SQLite mounted on a named volume
- JWT signing key is supplied via environment variable
- the Node gateway reaches the API over an internal network name
- compose is for local/demo deployment, not for final Azure production hosting

## What changes for real cloud deployment

For a production-oriented deployment, expect to replace or override:

- SQLite with SQL Server / Azure SQL
- local JWT signing key with a real secret source
- compose networking with the target platform’s service discovery / ingress model
- local persistence volume with managed database storage

## Why this is still useful

These artifacts make the repo easier to:

- demo as a multi-service integration system
- validate environment assumptions early
- transition toward App Service, Container Apps, or Kubernetes later
