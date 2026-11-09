# Build stage: restore with the manifest first so layer caching survives code edits.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Datara.sln ./
COPY src/Datara.Domain/Datara.Domain.csproj src/Datara.Domain/
COPY src/Datara.Application/Datara.Application.csproj src/Datara.Application/
COPY src/Datara.Infrastructure/Datara.Infrastructure.csproj src/Datara.Infrastructure/
COPY src/Datara.Api/Datara.Api.csproj src/Datara.Api/
COPY tests/Datara.UnitTests/Datara.UnitTests.csproj tests/Datara.UnitTests/
COPY tests/Datara.IntegrationTests/Datara.IntegrationTests.csproj tests/Datara.IntegrationTests/

RUN dotnet restore Datara.sln

COPY . .

RUN dotnet publish src/Datara.Api/Datara.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Runtime stage.
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Run unprivileged. The storage directory is created up front and chowned so the
# non-root user can write without needing a volume-level permission fix.
RUN adduser --system --uid 1001 --group datara \
    && mkdir -p /var/lib/datara/storage \
    && chown -R datara:datara /var/lib/datara

COPY --from=build /app/publish ./

USER datara

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=1 \
    FileStorage__RootPath=/var/lib/datara/storage

EXPOSE 8080

ENTRYPOINT ["dotnet", "Datara.Api.dll"]
