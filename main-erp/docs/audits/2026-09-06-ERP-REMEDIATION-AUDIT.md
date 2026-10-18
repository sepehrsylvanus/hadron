# Hadron ERP Remediation Audit — 2026-09-06

## Scope

This audit covers the Hadron ERP platform excluding the removed Store concept.
It reviews backend reliability and authorization, frontend resilience and
performance, the approved desktop UX patterns, production deployment
foundations, and restrained use of the Hadron companion character.

## Remediation completed

- Replaced the global no-op operational audit sink with a PostgreSQL-backed,
  append-only store. Database triggers reject update and delete operations.
- Added the protected, paged operational-audit worklist without exposing raw
  before/after payloads.
- Added centralized safe problem responses, trace IDs, isolated page/card
  failures, and a React application error boundary.
- Added login/bootstrap rate limiting and persistent Data Protection keys.
- Distinguished an unavailable API from an anonymous or expired session; only
  explicit invalid-session responses now clear the current login.
- Split liveness from readiness. Readiness now fails closed when either
  PostgreSQL or the required personnel-photo object store is unavailable.
- Added an actual HTTP restart-continuity test for the chunked Identity cookie,
  persisted Data Protection key ring, and server-side user session.
- Added a compensating write lease for personnel-photo uploads so an ordinary
  database failure does not leave a newly uploaded object orphaned; cleanup is
  bounded and failures are logged.
- Changed new non-administrator accounts to fail-closed Data Scope defaults.
- Added explicit immutable permission metadata to every mapped business API;
  URL-prefix permission inference was removed. Unregistered mapped APIs fail
  closed while absent routes and unsupported methods retain 404/405 semantics.
- Split frontend pages into lazy-loaded chunks. The application entry bundle is
  approximately 242 KB minified / 72 KB gzip instead of a single ~1.0 MB bundle.
- Reworked Product display, BOM actions and terminology, Forecast list/detail
  and editing, Sourcing tree/inspector, and MRP run presentation around the
  approved enterprise UX and Hadron visual system.
- Added unsaved-change protection to Product, Material, Supplier, and Sourcing
  object forms for application links and browser refresh/close.
- Standardized critical dialogs with least-destructive initial focus, focus
  trapping, Escape handling, background scroll locking, and focus restoration.
- Added table-header scope, required-field relationships, busy/live states,
  complete keyboard focus treatment, and regression checks for unresolved CSS
  design-token references.
- Added production Docker images, Caddy/Nginx configuration, isolated networks,
  persistent volumes, explicit migration execution, backup/restore scripts,
  health checks, and rollback documentation.
- Added the official Hadron companion to welcome/bootstrap/service states and
  long-running MRP refresh only. It is decorative, non-blocking, and excluded
  from operational tables, approvals, security prompts, and destructive flows.

## Verification evidence

- Backend build: 0 errors, 0 warnings.
- Backend integration suite: 329/329 passed after the final reliability changes.
- Permission/routing regression subset: 12/12 passed after preserving 404/405.
- Frontend tests: 111/111 passed.
- Frontend lint: passed.
- Frontend production build: passed without the previous monolithic chunk
  warning.
- Operational-audit migration `20260906090340_AddOperationalAuditStore` applied
  successfully to the Development database.
- Production Compose configuration and real API/UI/migration images validated.
- Human browser checks completed for Launchpad, Product list/detail and product
  structure, BOM list/tree and release guidance, Forecast list/month table and
  modal editing, Sourcing tree/inspector, and the current MRP result.
- Continuous Forecast typing was verified with three successive keystrokes;
  focus remained in the field and the change was cancelled without persistence.
- Quantity units were visually verified in the BOM tree (`pcs` and `kg`).
- The authenticated browser session remained valid across the final API process
  restart; both `/health/live` and the PostgreSQL/MinIO-backed `/health/ready`
  returned HTTP 200 afterwards.
- The protected security/operational Audit center and current automatic MRP
  result were reopened after restart. Security history paged correctly, the
  operational empty state was explicit, and the current MRP result remained
  complete with zero calculation exceptions.
- At the 1024 px constrained desktop check, the shell header now compresses
  without wrapping `Hadron ERP System`, while the signed-in user's name and
  photo remain visible.

## Character usage decision

Approved uses:

- Welcome/present pose on Login and the ERP Launchpad.
- Thinking pose during a real long-running calculation or background refresh.
- A future pointing/guide pose for first-use empty states and contextual help.
- A future calm/confused pose only for recoverable guidance, never a critical
  business or security error.

Prohibited uses:

- Repeated decoration in tables, trees, cards, or navigation.
- Finance, authorization, approval, destructive confirmation, or critical
  exception messaging.
- Replacing status text, progress, validation, or accessible labels.

The canonical implementation rules are in
`docs/brand/HADRON-CHARACTER-UI-GUIDELINES.md`.

## Honest score

- Development/UAT functional reliability: **9.4/10**
- Desktop UI/UX for the audited primary flows: **9.2/10**
- Production operations readiness: **8.6/10**

The lower production score is intentional. A successful build or visual check
does not prove operational recovery or legally complete audit delivery.

## Remaining production gates

1. Operational audit rows are durable after acceptance, but 16 services and 105
   semantic audit calls still use a locked post-commit, best-effort contract.
   Guaranteed delivery requires an approved Transactional Audit Outbox ADR and
   staged migration, starting with Inventory, Receipt, Purchase Order, and BOM
   approval. A generic SaveChanges interceptor is not acceptable because it
   loses semantic action/reason and creates noisy duplicates.
2. Run a destructive restore drill on Staging for PostgreSQL, MinIO, and Data
   Protection keys; backup scripts alone are not recovery evidence.
3. Add a maintained browser E2E/accessibility/visual-regression suite for the
   critical Forecast → BOM → Inventory → MRP → PO path.
4. Complete measured server-side grid virtualization/licensing work before very
   large operational datasets are accepted for production.
5. Reduce the remaining large shared services/DbContext coupling by vertical
   slice, without changing locked business behavior.
6. Migrate from `BrowserRouter` to the React Router data-router APIs before
   promising an in-app discard guard for the browser History Back action. Link
   navigation and browser refresh/close are already protected.
7. Migrate the remaining bespoke Identity administration modal/drawer and the
   Material Planning trace drawer to the shared accessible dialog primitive in
   a focused, regression-tested refactor.
