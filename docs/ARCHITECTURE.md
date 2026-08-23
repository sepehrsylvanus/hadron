# Current Architecture

## Scope

The implemented foundation is a modular-monolith host with a React frontend, one ASP.NET Core API, and one PostgreSQL 18 development database. MRP-01 through MRP-06 are `CLOSED`; they provide Reference Data, Product Master, Supplier Master, Part/Component Master, Sourcing and Commercial Terms, and Multi-Level BOM. MRP-07 has not started.

```text
React + TypeScript + Vite
          |
    ASP.NET Core API
          |
      PostgreSQL 18
```

The frontend and API run directly on the developer machine. Docker Compose currently manages PostgreSQL only. Configuration comes from environment variables; production hosts, domains, and secrets are not defined.

## Backend

`src/backend/Hadron.Erp.Api` is the single application host. It provides startup configuration, built-in Problem Details exception handling, development logging, PostgreSQL connectivity, and liveness/readiness endpoints. Implemented business modules cover Reference Data, Catalog/Product and Part/Component masters, Supplier Master, Sourcing, and BOM; they reuse the existing `HadronDbContext` while retaining explicit domain ownership.

Reference Data mutations publish through the platform-oriented `IAuditEventSink` boundary. The current host registers a no-op sink because shared Audit persistence is not implemented yet; no audit platform or audit history is claimed.

Future modules must own their domain rules, use cases, persistence mappings, permissions, and tests. Modules must not write directly to another module's tables or create circular dependencies.

## Frontend

`src/frontend` is an RTL-ready React application with screens for Reference Data, Products, Suppliers, Parts, Components, Sourcing, and BOM. Shared CSS variables hold current brand values. The existing navigation and screens are implementation reality, not the approved final application shell or completed UX refactor.

Official font assets are pending. Temporary system fallback fonts are documented in `docs/brand/UI-BRAND-RULES.md`.

## Canonical authority hierarchy

Business and domain behavior is governed by canonical module specifications and locked Decisions. Enterprise UX, navigation, shell, page patterns, and interaction behavior are governed by `docs/ui/HADRON-ERP-UX-ARCHITECTURE.md`. Digital visual UI, brand expression, design tokens, colors, logo, typography, spacing, and components are governed by `docs/brand/HADRON-DIGITAL-DESIGN-SYSTEM-v1.0.pdf`.

SAP Fiori is an interaction-pattern reference only and does not supply visual branding. Hadron visual identity remains authoritative. The product title is `Hadron ERP System`; MRP is a workspace within it, and MRP package numbers must never be exposed as user navigation. Neither UX nor visual authorities override canonical business rules.

## Database and local infrastructure

PostgreSQL 18 is the only containerized service. It uses a named development volume, environment-provided credentials, and a health check. Implemented persistence spans the owned schemas needed by MRP-01 through MRP-06, including Reference Data, Catalog, Organization, Procurement/Sourcing, and Engineering/BOM. No production deployment or backup design has been implemented.

Integration tests create isolated temporary databases named `hadron_erp_test_<32-hex>`. A database-name guard runs before destructive cleanup or drop and rejects Development or any unrecognized database name.

## Deferred capabilities

Identity, authorization, Hangfire, workers, outbox processing, MinIO, workflow, reporting, AG Grid, and AI integrations are approved future directions but are not part of the current architecture.

## Pending decisions

- `Docker Desktop License Review Required Before Organizational Rollout`.
- Confirm the official logo source because the current SVG uses `#BE1622` while the official UI brand red is `#CE1628`.
- Obtain licensed official Vazir and Myriad Pro font assets.
- Revisit Node.js 26 when it reaches LTS status.
