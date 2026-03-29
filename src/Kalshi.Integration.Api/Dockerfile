FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Build.targets Directory.Packages.props KalshiIntegrationSandbox.sln ./
COPY src/Kalshi.Integration.Api/Kalshi.Integration.Api.csproj src/Kalshi.Integration.Api/
COPY src/Kalshi.Integration.Application/Kalshi.Integration.Application.csproj src/Kalshi.Integration.Application/
COPY src/Kalshi.Integration.Contracts/Kalshi.Integration.Contracts.csproj src/Kalshi.Integration.Contracts/
COPY src/Kalshi.Integration.Domain/Kalshi.Integration.Domain.csproj src/Kalshi.Integration.Domain/
COPY src/Kalshi.Integration.Infrastructure/Kalshi.Integration.Infrastructure.csproj src/Kalshi.Integration.Infrastructure/

RUN dotnet restore src/Kalshi.Integration.Api/Kalshi.Integration.Api.csproj

COPY . .
RUN dotnet publish src/Kalshi.Integration.Api/Kalshi.Integration.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Kalshi.Integration.Api.dll"]
