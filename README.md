# Hadron Tavan ERP

Hadron Tavan ERP is a local-development modular-monolith foundation that will replace selected spreadsheet-based operational systems through small, validated vertical slices. The first implemented slice is the MRP Reference Data Foundation.

## Prerequisites

- .NET SDK 10.0.302
- Node.js 26.x and npm
- Docker Desktop with the WSL2 Linux-container backend
- Git

Node.js 26 is currently a Current release, not LTS. It is used by explicit project-owner instruction and must be reviewed when Node.js 26 enters LTS.

## Local setup

1. Create the local environment file:

   ```powershell
   Copy-Item .env.example .env
   ```

2. Replace `POSTGRES_PASSWORD` in `.env` with a local-only password.
3. Start PostgreSQL:

   ```powershell
   docker compose up -d postgres
   ```

4. Restore the local EF tool and apply development migrations, using the same password:

   ```powershell
   $env:POSTGRES_PASSWORD = '<local password>'
   dotnet tool restore
   dotnet tool run dotnet-ef database update --project src/backend/Hadron.Erp.Api --startup-project src/backend/Hadron.Erp.Api
   ```

5. Start the API in a terminal:

   ```powershell
   $env:POSTGRES_PASSWORD = '<local password>'
   dotnet run --project src/backend/Hadron.Erp.Api
   ```

6. Start the frontend in another terminal:

   ```powershell
   Set-Location src/frontend
   npm install
   npm run dev
   ```

The API uses `http://localhost:5080` in Development. Vite reads frontend variables from the repository-root `.env` file. Environment-specific hostnames and credentials are not embedded in application code.

## Verification commands

```powershell
dotnet restore Hadron.Erp.slnx
dotnet tool restore
dotnet build Hadron.Erp.slnx --no-restore
dotnet test Hadron.Erp.slnx --no-build
npm --prefix src/frontend run lint
npm --prefix src/frontend run build
docker compose ps
```

Integration tests create and remove dedicated databases using the controlled `hadron_erp_test_<32-hex>` pattern. They never clean the configured `hadron_erp` Development database. The configured PostgreSQL test user must be able to create and drop these temporary databases.

Readiness is exposed at `GET /health/ready`; liveness is exposed at `GET /health/live`.

## Current scope

The repository contains platform startup, PostgreSQL connectivity, health checks, integration-test infrastructure, and MRP-01 Reference Data. Reference Data provides fixed allowlisted types, configurable values, dedicated Currency and Unit models, optimistic concurrency, lifecycle APIs, and one RTL management screen. Product, Supplier, BOM, Forecast, Inventory, MRP calculation, Purchase Order, Identity, and Workflow remain out of scope.
