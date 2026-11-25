FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY NuGet.config ./
COPY Directory.Build.props ./
COPY games/ games/
COPY FortuneForge.Server/FortuneForge.Server.csproj FortuneForge.Server/
COPY FortuneForge.ServiceDefaults/FortuneForge.ServiceDefaults.csproj FortuneForge.ServiceDefaults/
RUN dotnet restore FortuneForge.Server/FortuneForge.Server.csproj

COPY FortuneForge.Server/ FortuneForge.Server/
COPY FortuneForge.ServiceDefaults/ FortuneForge.ServiceDefaults/
COPY config/backend-host.json config/backend-host.json
COPY config/game-maintenance.json config/game-maintenance.json
RUN dotnet publish FortuneForge.Server/FortuneForge.Server.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY artifacts/shared-backend-upload/game-packages/ ./game-packages/

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080
ENV GamePackages__Required=true
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "FortuneForge.Server.dll"]
