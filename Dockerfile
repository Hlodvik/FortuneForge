FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY FortuneForge.Server/FortuneForge.Server.csproj FortuneForge.Server/
COPY FortuneForge.ServiceDefaults/FortuneForge.ServiceDefaults.csproj FortuneForge.ServiceDefaults/
RUN dotnet restore FortuneForge.Server/FortuneForge.Server.csproj

COPY FortuneForge.Server/ FortuneForge.Server/
COPY FortuneForge.ServiceDefaults/ FortuneForge.ServiceDefaults/
RUN dotnet publish FortuneForge.Server/FortuneForge.Server.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "FortuneForge.Server.dll"]
