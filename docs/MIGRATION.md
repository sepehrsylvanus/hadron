# Hadron ERP migration handoff

This document defines the portable handoff package for moving the current
Hadron ERP development system to another computer or Codex workspace.

## Package contents

The qualified handoff ZIP contains:

- the complete meaningful working tree, including uncommitted and untracked
  source files, tests, migrations, documentation, brand assets and `AGENTS.md`;
- the local Git repository and its history;
- a current PostgreSQL custom-format dump with schema and data;
- a logical mirror of every current MinIO bucket;
- the Persian user handbook;
- Git status/diff metadata, tool versions, a file inventory and SHA-256
  checksums;
- a first-read handoff document with restore and validation steps.

Rebuildable or unsafe local state is intentionally excluded: `node_modules`,
`bin`, `obj`, build/test caches, runtime processes, logs, previous backups,
temporary files, `.env`, production secrets and ASP.NET Core Data Protection
keys. Excluding Data Protection keys invalidates existing browser sessions and
guest invitation tokens on the destination, but does not remove users,
password hashes, permissions, audit records or business data from PostgreSQL.

## Security classification

The ZIP is confidential even though plaintext application secrets are
excluded. Its database contains personnel profiles, password hashes, business
data, authorization state, messages and audit history. Its MinIO snapshot may
contain personnel photos and private collaboration attachments. Transfer it
only through access-controlled storage and delete unnecessary copies after the
restore is verified.

Never add `.env`, `deploy/production.env`, Data Protection keys, TLS private
keys, API tokens or passwords to this ZIP. Provision fresh secrets from the
included `.env.example` on the destination.

## Restore overview

1. Extract the ZIP and open `project/` as the repository root.
2. Read `AGENTS.md`, this document, `docs/ARCHITECTURE.md`,
   `docs/PERMISSIONS.md`, the relevant module decisions, the UX architecture
   and the digital brand rules before editing.
3. Install the versions recorded in `handoff/tool-versions.txt` and create a
   new `.env` from `.env.example` with new random secrets.
4. Start only PostgreSQL and MinIO:

   ```powershell
   docker compose up -d --wait postgres minio
   ```

5. From the extracted `project/` directory, run the guarded Windows restore
   helper. It verifies the complete signed file inventory, validates the
   PostgreSQL archive, stops the managed local writers and refuses to continue
   unless the destination database and corresponding MinIO buckets are empty.
   PostgreSQL is restored in one transaction with `--no-owner --no-acl`; MinIO
   object count and byte parity are checked before the application can restart:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/windows/Restore-HadronErpMigrationBundle.ps1 `
     -BundleRoot .. `
     -ConfirmRestore
   ```

   Use this only on a fresh, disposable Development/Staging installation. It is
   intentionally not an in-place Production restore tool. If any stage fails,
   the application remains stopped; discard/reset that isolated destination and
   retry rather than attempting to continue from partially restored storage.
6. Restore dependencies and build:

   ```powershell
   dotnet restore Hadron.Erp.slnx
   dotnet tool restore
   dotnet build Hadron.Erp.slnx --no-restore
   npm --prefix src/frontend ci
   npm --prefix src/frontend run lint
   npm --prefix src/frontend run build
   ```

7. Start the API and frontend using `README.md`. Existing users can sign in
   with their existing passwords, but every browser must create a new session.
8. Validate `/health/live`, `/health/ready`, login, permissions, profile
   photos, master data, BOM, Forecast, Inventory, MRP, Cash Flow and
   Collaboration before declaring the migration complete.
9. Rotate credentials again if the package was ever placed in a location
    whose access control cannot be proven.

Always restore into an isolated Development or Staging environment first.
Restore replaces database/object state and must never be run over a production
system without an approved backup and change window.
