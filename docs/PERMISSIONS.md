# Hadron ERP Identity, Profile and Permission Authority

## Status and authority

This document is the canonical platform authority for Hadron ERP authentication, user profiles, authorization and permission registration.

Status: `OWNER APPROVED — IMPLEMENTED 2026-08-26`

Business-module rules remain governed by their canonical module specifications and locked Decisions. A permission authorizes an operation; it never bypasses domain validation, lifecycle, concurrency, audit-history or database-integrity rules.

## Locked Owner decisions

1. Public registration is forbidden. A System Administrator creates every account and supplies its initial username and temporary password to the employee.
2. The employee must change the temporary password on first login.
3. A System Administrator can create another user with the complete `SystemAdministrator` authority, including user, role and permission administration.
4. Every ERP capability added in the future must register its permissions and enforce them in both the backend and the permission-aware UI.
5. Every username has one required personnel profile containing First Name, Last Name, Email Address, Cellphone Number, Organizational Unit and Personnel Photo.
6. First Name, Last Name, Email Address, Cellphone Number and Organizational Unit are required when an account is created. A personnel photo is required before the account can become active.
7. The signed-in employee's real full name and personnel photo are shown in the global shell. They come from the authenticated server session, not fake UI state or browser-local identity.
8. Personnel photos accept only validated JPEG, PNG or WebP content up to 2 MB. The application stores them through the approved S3-compatible storage abstraction; MinIO is the initial internal implementation.
9. The last active System Administrator cannot be deactivated or stripped of System Administrator authority.

## Identity boundaries

### System user

Authentication uses ASP.NET Core Identity with UUID technical identity and a secure Cookie-based session.

The Identity-owned account/profile contains at minimum:

- technical User ID
- unique normalized Username
- required First Name and Last Name
- required Email Address
- required Cellphone Number
- required Organizational Unit reference
- required Personnel Photo before activation
- Active/Inactive state
- MustChangePassword state
- Identity security/concurrency fields
- creation/update timestamps and actor context

Passwords, password-reset material, session tokens and security stamps are never returned through ordinary profile contracts or written to audit records.

### Internal person

`organization.internal_people` remains the Supplier Owner source defined by Supplier Master. It is not a login account and must not silently become an HR, payroll or Identity table.

An explicit optional association between a System User and an Internal Person may be supported when the same employee needs both identities. Neither record is implicitly created from the other.

### Organizational unit

Employee Organizational Unit is owned by a lightweight Organization master and is not the `BUSINESS_UNIT` ReferenceType used by Product Master.

The Organizational Unit master provides active/inactive historical resolution and optimistic concurrency. It must not introduce HR, payroll, attendance, job grading or a speculative organization-chart framework.

An active Organizational Unit is required for a new user assignment. A historical inactive unit remains resolvable on an existing profile, but an account cannot be newly activated against an inactive unit.

### Personnel photo

Photo bytes are not stored in PostgreSQL and are not accepted as a public arbitrary URL. PostgreSQL stores controlled metadata and the private object key. Photo reads require authentication; administrative reads and mutations require the relevant user-management permission.

The shell preserves the image aspect ratio and displays the authenticated user's full name and photo. The user menu provides profile, password-change and logout actions. No country flag or fake profile is introduced.

## Authentication workflow

1. A deployment-specific one-time bootstrap creates the first System Administrator. No default username or password is committed to source control.
2. A System Administrator creates the user, complete required profile, temporary password and access assignments.
3. The account may become active only after all mandatory profile fields and a valid personnel photo exist.
4. On first login, all business navigation is blocked until the temporary password is changed successfully.
5. Successful login establishes a secure HttpOnly Cookie session. State-changing requests use the platform CSRF protection policy.
6. Account deactivation or an explicit session-revoke action invalidates active sessions.
7. Authentication failures return `401`; authenticated users lacking authority receive `403` with a stable problem code.

Password policy, lockout thresholds and session lifetimes are configuration-owned security settings. They must have secure defaults and must not be hard-coded into business modules.

## Authorization model

Hadron ERP uses:

```text
Role permissions
+ direct user grants
- direct user denies
= effective permissions
```

For normal users, an explicit direct Deny takes precedence over Role and direct Grant. Permission codes are immutable machine identifiers; FA/EN labels are presentation data.

`SystemAdministrator` is the protected built-in administrative role:

- it receives every registered current and future permission;
- it can assign the same role to another user;
- it cannot be narrowed by a direct Deny while assigned;
- it does not bypass business validation, lifecycle, concurrency or database rules;
- its final active holder is protected by the last-administrator invariant.

Other roles are administrator-managed permission bundles. Role names do not replace permission checks. Sensitive actions such as Edit, Approve, Post, Reverse, Reopen and Permission Assignment remain separate.

## Data scope and business policy

RBAC answers what action is allowed. Data Scope answers which records are in scope. Business Policies answer whether the operation is valid in its current business state.

The typed Data Scope foundation exposes `ALL` as the universal scope and `WAREHOUSE:{WarehouseId}` for Inventory records. Warehouse scope is enforced by backend queries and commands: scoped users see only assigned warehouses, mutations require every affected warehouse to be in scope, and internal transfers require both source and destination. System Administrators always retain `ALL`. Company, Department, Project, Owner and approval-amount scopes remain deferred until their domains define reliable ownership facts.

UI visibility is not security. Navigation, routes and buttons use effective permissions for usability, while backend authorization remains the source of truth.

## Permission catalog

The following permission boundaries are the approved initial catalog. Implementation may add technically necessary read permissions only when they preserve these business boundaries and are documented here before release.

| Area | Permission codes |
|---|---|
| Identity and access | `identity.user.view`, `identity.user.manage`, `identity.password.reset`, `identity.role.view`, `identity.role.manage`, `identity.permission.assign`, `identity.session.revoke`, `identity.security_audit.view` |
| Own profile/session | `identity.profile.view_own`, `identity.password.change_own` |
| Organizational units | `organization.unit.view`, `organization.unit.manage` |
| Internal people | `organization.internal_person.view`, `organization.internal_person.manage` |
| Reference Data | `reference_data.view`, `reference_data.manage` |
| Products | `catalog.product.view`, `catalog.product.manage` |
| Parts | `catalog.part.view`, `catalog.part.manage` |
| Components | `catalog.component.view`, `catalog.component.manage` |
| Suppliers | `procurement.supplier.view`, `procurement.supplier.manage` |
| Sourcing | `procurement.sourcing.view`, `procurement.sourcing.manage`, `procurement.sourcing_preferred.manage`, `procurement.sourcing_lead_time.apply` |
| BOM | `engineering.bom.view`, `engineering.bom_draft.manage`, `engineering.bom.approve`, `engineering.bom.obsolete` |
| Forecast and Actual | `planning.forecast.view`, `planning.forecast.manage`, `production.actual.view`, `production.actual.manage`, `planning.period.close`, `planning.period.reopen` |
| Production execution | `production.order.view`, `production.order.manage`, `production.order.approve`, `production.order.release`, `production.material.stage`, `production.report.post` |
| Production routing | `production.work_center.view`, `production.work_center.manage`, `production.routing.view`, `production.routing.manage`, `production.routing.approve` |
| Inventory | `inventory.stock.view`, `inventory.warehouse.manage`, `inventory.opening.post`, `inventory.receipt.post`, `inventory.issue.post`, `inventory.transfer.post`, `inventory.adjustment.post`, `inventory.transaction.reverse`, `inventory.item_unit.manage` |
| MRP engine | `planning.mrp_run.view`, `planning.mrp_run.execute`, `planning.mrp_route.manage` |
| Material planning | `planning.material.view`, `planning.exception.view` |
| Order planning | `procurement.order_planning.view`, `procurement.order_planning.generate`, `procurement.order_planning.decide` |
| Purchase orders | `procurement.purchase_order.view`, `procurement.purchase_order_draft.manage`, `procurement.purchase_order.approve`, `procurement.purchase_order.return_to_draft`, `procurement.purchase_order.mark_ordered`, `procurement.purchase_schedule.manage`, `procurement.purchase_milestone.manage`, `procurement.purchase_schedule.cancel` |
| Purchase receipts | `procurement.purchase_receipt.view`, `procurement.purchase_receipt.create`, `procurement.purchase_receipt.post`, `procurement.purchase_receipt.reverse` |
| Analytics and reports | `analytics.view`, `analytics.forecast_accuracy.view`, `analytics.lead_time.view`, `analytics.financial_commitment.view`, `analytics.export` |
| Cash Flow | `cash_flow.dashboard.view`, `cash_flow.obligation.view`, `cash_flow.costing.view`, `cash_flow.reporting_rate.view`, `cash_flow.reporting_rate.manage`, `cash_flow.material_cost_book.view`, `cash_flow.material_cost_book.manage`, `cash_flow.material_cost_book.publish`, `cash_flow.purchase_price_history.view` |
| Enterprise Collaboration | `collaboration.workspace.view`, `collaboration.channel.manage`, `collaboration.message.send`, `collaboration.message.moderate`, `collaboration.file.share`, `collaboration.call.join`, `collaboration.call.start`, `collaboration.meeting.view`, `collaboration.meeting.schedule`, `collaboration.meeting.start`, `collaboration.meeting.join`, `collaboration.meeting.manage_participants`, `collaboration.meeting.end`, `collaboration.meeting.invite_guest`, `collaboration.meeting.report`, `correspondence.letter.view`, `correspondence.letter.manage`, `correspondence.letter.issue`, `correspondence.letter.acknowledge` |
| Trading operations | `trading.project.view`, `trading.project.manage`, `trading.project.close`, `trading.project.reopen`, `trading.reference.manage`, `trading.order_registration.view`, `trading.order_registration.manage`, `trading.fx.view`, `trading.fx.manage`, `trading.shipment.view`, `trading.shipment.manage`, `trading.customs.view`, `trading.customs.manage`, `trading.cost.view`, `trading.cost.manage`, `trading.report.view`, `trading.report.manage`, `trading.audit.view` |

Read dependencies required to render an authorized workflow must use bounded read contracts; they must not silently grant access to unrelated management screens or mutations.

Cash Flow is an isolated sensitive workspace. Dashboard, obligation, costing, reporting-rate, Material Cost Book and Purchase Price History permissions are independent and do not follow from Procurement, Analytics, Trading or BOM access. Conversely, a Cash Flow permission allows only its bounded Cash Flow contract and does not grant access to source-module screens or mutations. `cash_flow.reporting_rate.manage` authorizes Draft editing and explicit Publish of Cash Flow planning/reporting rates. `cash_flow.material_cost_book.manage` authorizes Draft editing only; `cash_flow.material_cost_book.publish` is a separate sensitive action. `cash_flow.purchase_price_history.view` is a read-only reporting permission and does not grant Purchase Order or Receipt access. None of these permissions is authority over accounting, trading, settlement or bank rates. Cash Flow export and payment execution are not current capabilities and have no permission codes.

Enterprise Collaboration permissions do not grant any operational ERP authority. `collaboration.message.send` covers the user's own text/sticker messages; moderation is separate. File sharing, call joining and call starting are separate capabilities. Formal correspondence viewing, Draft management, Issue and Acknowledge are separate permissions and additionally enforce sender/recipient policy. Private channel membership is always checked in the backend.

Meeting V1 permissions are independent, immutable capabilities. Possessing one does not imply any of the others, and meeting membership, assigned meeting role, lifecycle state and waiting-room policy remain additional backend checks:

- `collaboration.meeting.view`: view meetings that the user is otherwise allowed to access;
- `collaboration.meeting.schedule`: create or reschedule a meeting;
- `collaboration.meeting.start`: open a scheduled or instant meeting;
- `collaboration.meeting.join`: request or perform an authorized join;
- `collaboration.meeting.manage_participants`: admit, deny and manage participant roles where meeting policy permits;
- `collaboration.meeting.end`: end a meeting for all participants;
- `collaboration.meeting.invite_guest`: create or revoke external-guest invitations;
- `collaboration.meeting.report`: view authorized meeting and attendance reports.

Guest invitation authority does not grant the guest any ERP permission. Guest entry must use the dedicated expiring invitation and meeting-admission contract; it must not create an ERP user or bypass backend meeting policy.

## Mandatory registration contract for every future capability

A new ERP feature is incomplete unless the same change set includes all applicable items:

1. a unique immutable PermissionDefinition with FA/EN labels and business-area grouping;
2. backend authorization metadata on every protected endpoint/command;
3. permission-aware frontend route, navigation and action metadata;
4. inclusion in the System Administrator effective catalog;
5. visibility in the administrator permission matrix;
6. authorization tests for allowed, unauthenticated and forbidden paths;
7. an architecture test that rejects any business endpoint without explicit permission metadata;
8. audit coverage for sensitive mutations and access-administration changes;
9. documentation updates when a new permission boundary is introduced.

Unknown or missing permission metadata fails closed. Health endpoints and the minimum authentication/bootstrap endpoints are the only explicit anonymous exceptions.

### Material Planning collaboration permissions

The Planner Inbox is an operational annotation layer over immutable MRP Run exceptions. Its
permissions never authorize editing, resolving, suppressing or deleting an Exception. Private
saved views and private What-If scenarios also have explicit boundaries; What-If never authorizes
an official MRP Run, Purchase Order or Inventory mutation.

- `planning.work_item.view`
- `planning.work_item.manage`
- `planning.work_item.acknowledge`
- `planning.material_saved_view.manage`
- `planning.what_if.view`
- `planning.what_if.manage`

## Administration UX

The System Administration workspace is available only to authorized users and contains:

- User worklist
- Create/Edit User profile
- Account activation/deactivation
- Temporary password reset
- Role management
- Permission matrix grouped by business area
- Direct Grant/Deny overrides
- Effective-access preview
- Active-session revocation
- Security access history

The user editor must clearly separate Account, Personnel Profile, Roles, Direct Permissions, Data Scope, Sessions and Security History. Password values are write-only and never redisplayed.

## Audit and privacy

Identity and access administration is durable security history. At minimum record actor, target user, timestamp, action, changed role/permission/profile field, correlation identifier and source. Never record raw passwords, reset tokens, session cookies or photo bytes.

Personal Email, Cellphone and Personnel Photo are not general master-data fields. Ordinary users can access their own profile; administrative access is controlled and auditable.

## Implementation gates

Identity implementation cannot be considered complete until:

- existing business data and history are preserved by forward migrations;
- all current business endpoints are default-deny and explicitly mapped;
- authenticated session, first-login password change and last-admin protection are proven;
- a System Administrator can create another fully equivalent System Administrator;
- menu, routes and action visibility match effective permissions in FA/EN and RTL/LTR;
- direct URL and direct API access are blocked without permission;
- future unregistered endpoints fail architecture tests;
- MinIO photo upload/read validation and authorization are tested;
- no MRP, Inventory, Procurement, Receipt, Analytics or Golden Dataset semantics change.
