# Build context is the repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore first so that source edits do not invalidate the package layer.
COPY src/InventoryHold.Contracts/*.csproj src/InventoryHold.Contracts/
COPY src/InventoryHold.Domain/*.csproj src/InventoryHold.Domain/
COPY src/InventoryHold.Infrastructure/*.csproj src/InventoryHold.Infrastructure/
COPY src/InventoryHold.WebApi/*.csproj src/InventoryHold.WebApi/
RUN dotnet restore src/InventoryHold.WebApi/InventoryHold.WebApi.csproj

COPY src/ src/
RUN dotnet publish src/InventoryHold.WebApi/InventoryHold.WebApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# curl is only here for the container healthcheck.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "InventoryHold.WebApi.dll"]
