# Build context: the repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Project files first: the restore layer stays cached until a package reference changes.
COPY src/LibraryApp.Domain/LibraryApp.Domain.csproj src/LibraryApp.Domain/
COPY src/LibraryApp.Application/LibraryApp.Application.csproj src/LibraryApp.Application/
COPY src/LibraryApp.Infrastructure/LibraryApp.Infrastructure.csproj src/LibraryApp.Infrastructure/
COPY src/LibraryApp.Api/LibraryApp.Api.csproj src/LibraryApp.Api/
RUN dotnet restore src/LibraryApp.Api/LibraryApp.Api.csproj

COPY src/ src/
RUN dotnet publish src/LibraryApp.Api/LibraryApp.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Serilog writes to logs/ (appsettings.json); the non-root user of the image needs write access there.
RUN mkdir -p /app/logs && chown $APP_UID /app/logs
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "LibraryApp.Api.dll"]
