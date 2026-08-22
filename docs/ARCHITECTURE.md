# Current Architecture

## Scope

The implemented foundation is a modular-monolith host with a React frontend, one ASP.NET Core API, and one PostgreSQL 18 development database. MRP-01 adds the first bounded business foundation: Reference Data.

```text
React + TypeScript + Vite
          |
    ASP.NET Core API
          |
      PostgreSQL 18
```

The frontend and API run directly on the developer machine. Docker Compose currently manages PostgreSQL only. Configuration comes from environment variables; production hosts, domains, and secrets are not defined.

## Backend

`src/backend/Hadron.Erp.Api` is the single application host. It provides startup configuration, built-in Problem Details exception handling, development logging, PostgreSQL connectivity, liveness/readiness endpoints, and the MRP-01 Reference Data API. Reference Data is organized under `Modules/ReferenceData` and reuses the existing `HadronDbContext`.

Reference Data mutations publish through the platform-oriented `IAuditEventSink` boundary. The current host registers a no-op sink because shared Audit persistence is not implemented yet; no audit platform or audit history is claimed.

Future modules must own their domain rules, use cases, persistence mappings, permissions, and tests. Modules must not write directly to another module's tables or create circular dependencies.

## Frontend

`src/frontend` is an RTL-ready React application. Shared CSS variables hold approved brand values. Its current business screen is the minimal Persian-first Reference Data manager; it is not the final application shell or a complete design system.

Official font assets are pending. Temporary system fallback fonts are documented in `docs/brand/UI-BRAND-RULES.md`.

## Database and local infrastructure

PostgreSQL 18 is the only containerized service. It uses a named development volume, environment-provided credentials, and a health check. The `reference_data` schema contains fixed Reference Type definitions, configurable Reference Values, Currency, and Unit. No production deployment or backup design has been implemented.

Integration tests create isolated temporary databases named `hadron_erp_test_<32-hex>`. A database-name guard runs before destructive cleanup or drop and rejects Development or any unrecognized database name.

## Deferred capabilities

Identity, authorization, Hangfire, workers, outbox processing, MinIO, workflow, reporting, AG Grid, and AI integrations are approved future directions but are not part of the current architecture.

## Pending decisions

- `Docker Desktop License Review Required Before Organizational Rollout`.
- Confirm the official logo source because the current SVG uses `#BE1622` while the official UI brand red is `#CE1628`.
- Obtain licensed official Vazir and Myriad Pro font assets.
- Revisit Node.js 26 when it reaches LTS status.
