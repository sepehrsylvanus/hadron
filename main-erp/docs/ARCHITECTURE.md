# Current Architecture

## Scope

The implemented foundation is a modular-monolith host with a React frontend, one ASP.NET Core API, and one PostgreSQL 18 development database. MRP-01 through MRP-14 are `CLOSED`; they provide Reference Data, Product Master, Supplier Master, Part/Component Master, Sourcing and Commercial Terms, Multi-Level BOM, Forecast/Actual, Inventory Foundation, Purchase Supply Foundation, the MRP Calculation Engine, the Material Planning UI, Order Planning, Purchase Order Operations, and Analytics/Reporting Integration.

```text
React + TypeScript + Vite
          |
    ASP.NET Core API
          |
      PostgreSQL 18
```

The frontend and API run directly on the developer machine. Docker Compose manages PostgreSQL and the private MinIO personnel-photo store. Configuration comes from environment variables; production hosts, domains, and secrets are not defined.

## Backend

`src/backend/Hadron.Erp.Api` is the single application host. It provides startup configuration, built-in Problem Details exception handling, development logging, PostgreSQL connectivity, and liveness/readiness endpoints. Implemented business modules cover Reference Data, Catalog/Product and Part/Component masters, Supplier Master, Sourcing, BOM, Forecast/Actual Planning, Inventory Foundation, and Purchase Supply Foundation; they reuse the existing `HadronDbContext` while retaining explicit domain ownership.

Reference Data mutations publish through the platform-oriented `IAuditEventSink` boundary. The current host registers a no-op sink because a shared cross-module Audit platform is not implemented yet. Identity owns its required append-only security-administration history; this module-owned history is not a substitute for the future shared Audit platform.

Future modules must own their domain rules, use cases, persistence mappings, permissions, and tests. Every capability must follow the central registration and default-deny rules in `docs/PERMISSIONS.md`; feature-local Identity or authorization is forbidden. Modules must not write directly to another module's tables or create circular dependencies.

Cash Flow is a separate permission-isolated workspace. Its obligation and costed-BOM surfaces use query-time backend read models over bounded Purchase Supply, Supplier, Catalog and Engineering/BOM facts and never update those source modules. The owner-approved USD reporting extension persists only immutable/versioned Cash Flow Reporting FX Rate Sets and produces traceable USD management totals while preserving every original amount and currency. Those rates are planning/reporting assumptions, not accounting, trading, settlement or bank rates. Landed cost, weighted-average costing, export and payment execution remain outside the extension. Canonical contracts are `docs/modules/cash-flow/CASH-FLOW-V1.md` and `docs/modules/cash-flow/CASH-FLOW-USD-REPORTING.md`.

Enterprise Collaboration is an independent module and workspace. It owns channels, direct conversations, messages, reactions, formal correspondence metadata, call sessions and attachment metadata under schema `collaboration`. It reads only a bounded active-user directory from Identity and writes no operational-module data. Realtime updates use SignalR, private files use the S3-compatible storage abstraction, and voice/video rooms use optional self-hosted LiveKit. Its canonical contract is `docs/modules/collaboration/COLLABORATION-V1.md`.

## Frontend

`src/frontend` is a bilingual FA/EN React application with RTL/LTR switching, the Hadron ERP shell, routed List/Object page patterns, and screens for Reference Data, Products, Suppliers, Parts, Components, Sourcing, and BOM. Shared UI primitives and brand tokens implement the approved UX/UI foundation; module-specific business behavior remains governed by canonical module specifications.

The Cash Flow workspace is not nested under MRP, Analytics or Trading navigation. Dashboard, payment-obligation, costed-BOM and reporting-rate routes use explicit independent permissions; navigation visibility does not replace backend authorization.

The Collaboration workspace is a peer workspace, not an MRP submenu. Messages, formal correspondence and calls are separate task routes. Collaboration routes and actions are permission-aware; all enforcement remains server-side.

Official font assets are pending. Temporary system fallback fonts are documented in `docs/brand/UI-BRAND-RULES.md`.

## Canonical authority hierarchy

Business and domain behavior is governed by canonical module specifications and locked Decisions. Enterprise UX, navigation, shell, page patterns, and interaction behavior are governed by `docs/ui/HADRON-ERP-UX-ARCHITECTURE.md`. Digital visual UI, brand expression, design tokens, colors, logo, typography, spacing, and components are governed by `docs/brand/HADRON-DIGITAL-DESIGN-SYSTEM-v1.0.pdf`.

SAP Fiori is an interaction-pattern reference only and does not supply visual branding. Hadron visual identity remains authoritative. The product title is `Hadron ERP System`; MRP and Production are separate workspaces within it. Production v1 exposes only Daily Production Actual operations while Forecast remains under MRP, and MRP package numbers must never be exposed as user navigation. Neither UX nor visual authorities override canonical business rules.

## Database and local infrastructure

PostgreSQL 18 and MinIO are containerized local services. Both use named development volumes, environment-provided credentials and health checks. Implemented persistence spans the owned schemas needed by MRP-01 through MRP-14 plus Identity/Profile/Permission state, Organization Units, sessions and append-only security history. Personnel photos use the private S3-compatible storage abstraction backed by MinIO. Production deployment and backup policy remain required before organizational rollout.

Integration tests create isolated temporary databases named `hadron_erp_test_<32-hex>`. A database-name guard runs before destructive cleanup or drop and rejects Development or any unrecognized database name.

## Deferred capabilities

Identity, authorization, personnel profiles, typed `ALL` plus backend-enforced `WAREHOUSE` Data Scope, session revocation and private MinIO photo storage are implemented according to `docs/PERMISSIONS.md` and `docs/ADR/ADR-001-IDENTITY-PERMISSION-PROFILE.md`. Other granular Data Scopes remain deferred until canonical record ownership exists. Inventory is a standalone ERP workspace with warehouse master, immutable physical ledger and two-step internal transfer; Logistics remains a separate future module. MRP input mutations enqueue a transactional `planning.mrp_refresh_outbox` record; one bounded hosted worker coalesces pending changes and atomically replaces the `CURRENT` persisted plan while the last audited prior-day plan remains in `CERTIFIED`. End-of-day certification writes a lightweight immutable certificate and replaces the prior certified full dataset; only explicit business-decision snapshots remain as additional immutable Runs. The operational dashboard reads only these persisted plan snapshots/results and uses physical usable warehouse stock; dispatched in-transit stock is excluded until destination receipt. Hangfire, general workflow, AG Grid and AI integrations remain approved future directions outside the current implementation.

## Pending decisions

- `Docker Desktop License Review Required Before Organizational Rollout`.
- Confirm the official logo source because the current SVG uses `#BE1622` while the official UI brand red is `#CE1628`.
- Obtain licensed official Vazir and Myriad Pro font assets.
- Revisit Node.js 26 when it reaches LTS status.
