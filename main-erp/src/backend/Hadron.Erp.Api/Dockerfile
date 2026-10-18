# syntax=docker/dockerfile:1.7

# Container builds use an explicitly selected SDK feature band. The repository
# global.json remains the workstation/CI toolchain authority, but is not copied
# here because its `latestPatch` policy intentionally rejects newer feature
# bands such as 10.0.400.
FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS restore
WORKDIR /source

COPY Directory.Build.props Hadron.Erp.slnx ./
COPY .config/dotnet-tools.json .config/dotnet-tools.json
COPY src/backend/Hadron.Erp.Api/Hadron.Erp.Api.csproj src/backend/Hadron.Erp.Api/
RUN dotnet restore src/backend/Hadron.Erp.Api/Hadron.Erp.Api.csproj

FROM restore AS build
COPY src/backend/Hadron.Erp.Api/ src/backend/Hadron.Erp.Api/
RUN dotnet publish src/backend/Hadron.Erp.Api/Hadron.Erp.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /artifacts/api \
    /p:UseAppHost=false

# The migration bundle is deliberately a separate image target. It is invoked
# only through the explicit "maintenance" Compose profile and never at API boot.
FROM build AS migration-bundle
ENV ASPNETCORE_ENVIRONMENT=Build \
    POSTGRES_HOST=postgres \
    POSTGRES_PORT=5432 \
    POSTGRES_DB=hadron_erp_build \
    POSTGRES_USER=hadron_build \
    POSTGRES_PASSWORD=build-only-placeholder \
    MINIO_ENDPOINT=http://minio:9000 \
    MINIO_ACCESS_KEY=build-only-placeholder \
    MINIO_SECRET_KEY=build-only-placeholder \
    MINIO_PROFILE_BUCKET=hadron-profile-photos
RUN dotnet tool restore
RUN dotnet tool run dotnet-ef migrations bundle \
    --project src/backend/Hadron.Erp.Api/Hadron.Erp.Api.csproj \
    --startup-project src/backend/Hadron.Erp.Api/Hadron.Erp.Api.csproj \
    --configuration Release \
    --output /artifacts/efbundle \
    --force

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-base
ARG HADRON_UID=10001
ARG HADRON_GID=10001

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && groupadd --gid "${HADRON_GID}" hadron \
    && useradd --uid "${HADRON_UID}" --gid "${HADRON_GID}" \
        --create-home --home-dir /home/hadron --shell /usr/sbin/nologin hadron \
    && install --directory --owner hadron --group hadron --mode 0700 \
        /home/hadron/.aspnet/DataProtection-Keys \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    HOME=/home/hadron
WORKDIR /app
EXPOSE 8080

FROM runtime-base AS runtime
COPY --from=build --chown=10001:10001 /artifacts/api/ ./
USER 10001:10001
ENTRYPOINT ["dotnet", "Hadron.Erp.Api.dll"]

FROM runtime-base AS migration-runtime
COPY --from=migration-bundle --chown=10001:10001 /artifacts/efbundle /app/efbundle
RUN chmod 0555 /app/efbundle
USER 10001:10001
ENTRYPOINT ["/app/efbundle"]
