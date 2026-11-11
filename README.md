# Hadron Tavan ERP

Hadron Tavan ERP is a local-development modular-monolith foundation that will replace selected spreadsheet-based operational systems through small, validated vertical slices. The implemented MRP v1 baseline covers MRP-01 through MRP-14, which are closed.

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

2. Replace `POSTGRES_PASSWORD`, `MINIO_ACCESS_KEY`, `MINIO_SECRET_KEY`, and
   `IDENTITY_BOOTSTRAP_TOKEN` in `.env` with local-only secrets.
3. Start PostgreSQL and the private personnel-photo store:

   ```powershell
   docker compose up -d postgres minio
   ```

4. Load the local environment, restore the local EF tool, and apply development migrations:

   ```powershell
   Get-Content .env | Where-Object { $_ -match '^[^#].*=.*$' } | ForEach-Object {
       $name, $value = $_ -split '=', 2
       Set-Item -Path "Env:$name" -Value $value
   }
   dotnet tool restore
   dotnet tool run dotnet-ef database update --project src/backend/Hadron.Erp.Api --startup-project src/backend/Hadron.Erp.Api
   ```

5. Start the API in the same terminal so PostgreSQL, MinIO, and one-time bootstrap
   configuration remain available:

   ```powershell
   dotnet run --project src/backend/Hadron.Erp.Api
   ```

6. Start the frontend in another terminal:

   ```powershell
   Set-Location src/frontend
   npm install
   npm run dev
   ```

The API uses `http://localhost:5080` in Development. Vite reads frontend variables from the repository-root `.env` file. The bootstrap token is accepted only while no Identity user exists; after the first administrator is created, normal Cookie authentication is required. Environment-specific hostnames and credentials are not embedded in application code.

### Interactive Windows launcher

After configuring `.env`, run `run-hadron.cmd` from the repository root. It starts
PostgreSQL and MinIO with Docker Compose, applies migrations, and runs the API
and frontend on loopback. It prints the frontend URL and the selected ports.
If a preferred port is occupied, the launcher selects the next available TCP
port and passes it to the dependent services; existing running Compose database
and MinIO bindings are reused. It does not change `.env` or stop unrelated
processes. Closing the launcher stops the API and frontend, but leaves Docker
containers and their data intact. Port selection is best-effort: another process
can acquire a port between checking and starting; rerun if that happens.

This launcher is for local development only: it uses local PostgreSQL and MinIO,
not custom remote endpoints, and does not start the optional LiveKit profile.
The separate managed company-LAN host below intentionally retains its fixed,
firewall-scoped ports. Do not use the two launch modes at the same time.
MinIO is built locally from checksum-verified upstream GitHub release binaries.
Docker Desktop must be able to reach GitHub release downloads and Docker Hub
(for the Alpine base image and PostgreSQL). The first build may take longer.

### Persistent local Windows test host

The manual terminals above are the default developer workflow. For an operator PC that must keep the local test system available after Codex or a terminal closes, publish the API to the isolated local-host runtime and register the two current-user Scheduled Tasks:

```powershell
$runtime = Join-Path (Get-Location) '.local-host\runtime\api'
dotnet publish src/backend/Hadron.Erp.Api -c Release -o $runtime
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/windows/Manage-HadronErpLocalHost.ps1 -Action Install
```

The tasks start 45 seconds after Windows logon. They keep running while that Windows account remains signed in (including while the screen is locked), but they do not run before sign-in and stop at Windows sign-out. Each task keeps an internal supervisor that waits indefinitely for local dependencies and restarts only its owned child after an unexpected exit. PID, executable-path, and command-line checks protect unrelated processes during a managed stop. Supervisor logs rotate at 10 MB, and child output logs are checked between restarts; all logs are kept under `%LOCALAPPDATA%\HadronErp\LocalHost\logs`. Persistent Data Protection keys use the same Windows user so login cookies survive an API restart. The host does not run migrations, restart Docker Desktop, expose the API, PostgreSQL or MinIO to the network, or terminate unrelated `dotnet`/`node` processes.

For the approved company-LAN test setup, only the Vite frontend listens on TCP 5173. Windows Firewall must restrict that listener to the Node.js executable, the host's Ethernet address, and the approved company subnet. The API remains on loopback and is reached through Vite's same-origin `/api` proxy. On the current reserved host address, another device on the same subnet uses `http://192.168.0.175:5173/login`. This HTTP/Vite path is for trusted LAN testing only and must never be port-forwarded to the Internet; production requires the built frontend, a reverse proxy, and HTTPS.

Set `VITE_PUBLIC_APP_ORIGIN=http://192.168.0.175:5173` in the local `.env` file and restart Vite before copying a guest-meeting invitation. This value must be the LAN-accessible **frontend** origin, never the loopback API origin. When the setting is omitted, copied invitations use the origin serving the current page, which is the normal Production behavior behind the HTTPS reverse proxy.

Run the LAN firewall configuration from an elevated PowerShell window. The same script can inspect or remove only its own firewall rule:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/windows/Configure-HadronErpCompanyLan.ps1 -Action Enable
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/windows/Configure-HadronErpCompanyLan.ps1 -Action Status
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/windows/Configure-HadronErpCompanyLan.ps1 -Action Disable
```

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/windows/Manage-HadronErpLocalHost.ps1 -Action Status
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/windows/Manage-HadronErpLocalHost.ps1 -Action Stop
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/windows/Manage-HadronErpLocalHost.ps1 -Action Start
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/windows/Manage-HadronErpLocalHost.ps1 -Action Uninstall
```

Stop the tasks before publishing a newer API build, then start them again. The frontend intentionally runs Vite from the working tree for local testing, so publish/restart the API whenever a backend contract changes to keep both sides aligned.

This is a local test-host reliability layer, not the supported Internet or production deployment. Production remains the Linux Docker Compose procedure in `docs/DEPLOYMENT.md`.

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

The repository contains platform startup, PostgreSQL connectivity, health checks, integration-test infrastructure, and the closed MRP-01 through MRP-14 modules: Reference Data, Product Master, Supplier Master, Part/Component Master, Sourcing and Commercial Terms, Multi-Level BOM, Forecast/Actual, Inventory Foundation, Purchase Supply Foundation, MRP Calculation Engine, Material Planning UI, Order Planning, Purchase Order Operations, and Analytics/Reporting Integration. Inventory is presented as its own ERP workspace with warehouse master, immutable physical ledger and two-step internal transfers; MRP consumes its usable physical company-wide position while in-transit stock remains excluded until receipt. The Hadron ERP shell, bilingual FA/EN UI foundation, shared enterprise page patterns, central Identity, required personnel profiles, private photo storage, Cookie sessions and default-deny permission plus Warehouse Data Scope foundation are implemented. Workflow and Logistics remain outside the current implementation scope.
