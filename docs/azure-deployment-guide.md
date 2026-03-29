# Azure deployment guide

This guide documents a credible Azure deployment path for the Kalshi Integration Sandbox.

## Recommended Azure target

Use **Azure Container Apps** for both services:

- **kalshi-integration-api** → ASP.NET Core API container
- **kalshi-integration-gateway** → Node gateway container

Why this target fits the repo well:

- the repo already has Dockerfiles for both services
- multi-service deployment is straightforward
- environment variables and secrets map cleanly to the current configuration model
- HTTP ingress between services is simple
- it is a stronger production-style story than local compose without forcing Kubernetes complexity

## Required Azure services

Minimum recommended resource set:

- **Azure Container Registry (ACR)**
  - stores the API and gateway images
- **Azure Container Apps Environment**
  - shared environment for the two services
- **Azure Container App: API**
  - public ingress enabled
- **Azure Container App: Gateway**
  - public ingress optional depending on demo needs
- **Azure SQL Database**
  - primary relational database for cloud deployment
- **Azure Key Vault**
  - store JWT signing key and any sensitive connection details
- **Azure Log Analytics Workspace**
  - backing store for Container Apps logs/diagnostics

Optional later:

- **Application Insights / Azure Monitor** for richer telemetry
- **Azure Service Bus or RabbitMQ** if the event publishing path is expanded beyond the in-memory/default path

## Target architecture

```text
Internet
  -> Azure Container App: kalshi-integration-gateway (optional public ingress)
  -> Azure Container App: kalshi-integration-api (public ingress for API/demo)
       -> Azure SQL Database
       -> Azure Key Vault

Azure Container Registry
  -> stores API and gateway images used by Container Apps
```

## Deployment assumptions

This guide assumes:

- the API uses `Database__Provider=AzureSql`
- the Node gateway calls the API using the API container app URL
- secrets are injected from Azure-managed configuration, not committed files
- schema updates can be applied by enabling startup migrations or by running `dotnet ef database update` in a controlled release step

## One-time Azure setup

Example resource names:

- Resource group: `rg-kalshi-sandbox`
- Location: `centralus`
- ACR: `kalshisandboxacr`
- Container Apps environment: `kalshi-sandbox-env`
- Log Analytics workspace: `law-kalshi-sandbox`
- Key Vault: `kv-kalshi-sandbox`
- SQL server: `sql-kalshi-sandbox`
- SQL database: `kalshi-integration-sandbox`

### 1) Create the resource group

```bash
az group create \
  --name rg-kalshi-sandbox \
  --location centralus
```

### 2) Create the Azure Container Registry

```bash
az acr create \
  --resource-group rg-kalshi-sandbox \
  --name kalshisandboxacr \
  --sku Basic
```

### 3) Create Log Analytics + Container Apps environment

```bash
az monitor log-analytics workspace create \
  --resource-group rg-kalshi-sandbox \
  --workspace-name law-kalshi-sandbox

WORKSPACE_ID=$(az monitor log-analytics workspace show \
  --resource-group rg-kalshi-sandbox \
  --workspace-name law-kalshi-sandbox \
  --query customerId -o tsv)

WORKSPACE_KEY=$(az monitor log-analytics workspace get-shared-keys \
  --resource-group rg-kalshi-sandbox \
  --workspace-name law-kalshi-sandbox \
  --query primarySharedKey -o tsv)

az containerapp env create \
  --name kalshi-sandbox-env \
  --resource-group rg-kalshi-sandbox \
  --location centralus \
  --logs-workspace-id "$WORKSPACE_ID" \
  --logs-workspace-key "$WORKSPACE_KEY"
```

### 4) Create Azure SQL

```bash
az sql server create \
  --name sql-kalshi-sandbox \
  --resource-group rg-kalshi-sandbox \
  --location centralus \
  --admin-user kalshiadmin \
  --admin-password '<set-a-strong-password>'

az sql db create \
  --resource-group rg-kalshi-sandbox \
  --server sql-kalshi-sandbox \
  --name kalshi-integration-sandbox \
  --service-objective Basic
```

Allow Azure services during early setup/demo if needed:

```bash
az sql server firewall-rule create \
  --resource-group rg-kalshi-sandbox \
  --server sql-kalshi-sandbox \
  --name AllowAzureServices \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0
```

### 5) Create Key Vault and store secrets

```bash
az keyvault create \
  --name kv-kalshi-sandbox \
  --resource-group rg-kalshi-sandbox \
  --location centralus

az keyvault secret set \
  --vault-name kv-kalshi-sandbox \
  --name jwt-signing-key \
  --value 'replace-with-a-long-production-secret'
```

## Build and push images

Login to ACR:

```bash
az acr login --name kalshisandboxacr
```

Build and push the API image:

```bash
docker build \
  -f src/Kalshi.Integration.Api/Dockerfile \
  -t kalshisandboxacr.azurecr.io/kalshi-integration-api:latest \
  .

docker push kalshisandboxacr.azurecr.io/kalshi-integration-api:latest
```

Build and push the gateway image:

```bash
docker build \
  -f node-gateway/Dockerfile \
  -t kalshisandboxacr.azurecr.io/kalshi-integration-gateway:latest \
  .

docker push kalshisandboxacr.azurecr.io/kalshi-integration-gateway:latest
```

## Deploy the API container app

```bash
az containerapp create \
  --name kalshi-integration-api \
  --resource-group rg-kalshi-sandbox \
  --environment kalshi-sandbox-env \
  --image kalshisandboxacr.azurecr.io/kalshi-integration-api:latest \
  --target-port 8080 \
  --ingress external \
  --registry-server kalshisandboxacr.azurecr.io \
  --env-vars \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    Database__Provider=AzureSql \
    ConnectionStrings__KalshiIntegration='Server=tcp:sql-kalshi-sandbox.database.windows.net,1433;Initial Catalog=kalshi-integration-sandbox;Persist Security Info=False;User ID=kalshiadmin;Password=<from-secret-store>;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' \
    Authentication__Jwt__Issuer='kalshi-integration-sandbox' \
    Authentication__Jwt__Audience='kalshi-integration-sandbox-clients' \
    Authentication__Jwt__EnableDevelopmentTokenIssuance=false \
    OpenApi__EnableSwaggerInNonDevelopment=false
```

Then set the JWT signing key as a secret-backed setting using your preferred secure method. The important rule is: **do not commit or hardcode the production signing key**.

## Deploy the gateway container app

First get the API URL:

```bash
API_URL=$(az containerapp show \
  --name kalshi-integration-api \
  --resource-group rg-kalshi-sandbox \
  --query properties.configuration.ingress.fqdn -o tsv)
```

Then create the gateway app:

```bash
az containerapp create \
  --name kalshi-integration-gateway \
  --resource-group rg-kalshi-sandbox \
  --environment kalshi-sandbox-env \
  --image kalshisandboxacr.azurecr.io/kalshi-integration-gateway:latest \
  --target-port 3001 \
  --ingress external \
  --registry-server kalshisandboxacr.azurecr.io \
  --env-vars \
    PORT=3001 \
    BACKEND_BASE_URL="https://$API_URL"
```

## Verification steps

Check API health:

```bash
curl -s https://$API_URL/health/live
curl -s https://$API_URL/health/ready
```

Get the gateway URL:

```bash
GATEWAY_URL=$(az containerapp show \
  --name kalshi-integration-gateway \
  --resource-group rg-kalshi-sandbox \
  --query properties.configuration.ingress.fqdn -o tsv)
```

Check gateway health:

```bash
curl -s https://$GATEWAY_URL/health
```

## Release/update workflow

Recommended repeatable workflow:

1. validate locally using the existing repo commands
2. build new container images
3. push images to ACR
4. update the Container Apps revision to the new image tag
5. verify `/health/live`, `/health/ready`, and gateway `/health`

## Pipeline alignment

The current Azure DevOps pipeline already validates:

- `dotnet format`
- `dotnet build`
- `dotnet test`
- `node --test`

That means the missing step for future automation is deployment orchestration, not basic build validation.

## Important notes

- use Azure SQL for cloud deployment instead of SQLite
- keep JWT signing keys and connection strings in secret/config stores
- keep Swagger disabled outside development unless there is an explicit reason to expose it
- prefer Container Apps for the current repo shape; move to App Service or Kubernetes only if the deployment requirements change materially
