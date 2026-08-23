# AGENTS.md — Hadron Tavan ERP Platform

## 1. Project Mission

This repository is the foundation of **Hadron Tavan's long-term internal business software platform**.

This is not a generic demo ERP and must not become a collection of unrelated CRUD pages.

The long-term goal is to gradually replace fragmented internal systems currently implemented with:

* Excel
* Google Sheets
* Google Apps Script
* HTML tools
* standalone financial and operational files

with one fast, reliable, modular web platform.

The platform should eventually support capabilities such as:

* Product and Component Master Data
* BOM / Engineering
* MRP
* Forecast and Actual Production
* Inventory
* Procurement
* Order Planning
* Purchase Orders
* Supplier Management
* Trading
* Import and Logistics
* Shipment and Customs
* Finance Integration
* Sales
* CRM
* Quality
* Maintenance
* Documents
* Workflow
* Dashboards
* Reporting
* Automation
* AI Agents

Do **not** attempt to build all of these at once.

The system must grow through small, validated vertical slices.

---

# 2. Project Owner Context

The project owner understands Hadron Tavan's business processes deeply and has extensive experience designing operational logic in Excel and Google Sheets.

Requirements may therefore be described using:

* sheets
* rows
* columns
* formulas
* filters
* Excel-style calculations
* business scenarios

Do not blindly reproduce spreadsheet architecture in software.

Translate the business intent into proper:

* domain models
* database structures
* APIs
* workflows
* services
* reports
* user interfaces

If a spreadsheet-style requirement would create poor performance, duplication, weak data integrity, or architectural debt, explain the issue and propose a better software design.

Communicate with the project owner primarily in **Persian**, unless English is more appropriate for:

* code
* technical identifiers
* filenames
* database objects
* API contracts
* documentation terminology

Be direct and technical.

Do not add unnecessary praise or generic explanations.

---

# 3. Approved Architecture

The approved baseline architecture is:

**Modular Monolith**

Do not replace this architecture without a clear technical reason and explicit approval from the project owner.

The initial system should remain:

* one repository
* one coordinated application platform
* one PostgreSQL database cluster
* one controlled deployment

while maintaining strict boundaries between business modules.

Microservices are **not** the default architecture.

---

# 4. Approved Technology Stack

## Frontend

* React
* TypeScript
* Vite

## Business Data Grid

* AG Grid Enterprise

Use spreadsheet-like interaction where it provides operational value, especially for:

* MRP
* Planning
* Inventory
* Procurement
* Finance-related reporting

Relevant capabilities may include:

* server-side filtering
* sorting
* search
* column resize
* pinned/frozen columns
* inline editing where safe
* copy/paste where safe
* bulk actions
* Excel export
* saved views

Do not recreate Excel itself inside the browser.

## Backend

* ASP.NET Core
* .NET 10 LTS

## Database

* PostgreSQL 18

## ORM

* Entity Framework Core

## Authentication

* ASP.NET Core Identity
* secure Cookie-based session for the main web application

## Authorization

Use:

* RBAC
* Data Scope
* Business Policies

## Background Processing

Use:

* Transactional Outbox
* Hangfire
* Background Worker

## File Storage

Use an S3-compatible abstraction.

Initial internal deployment may use:

* MinIO

## Reporting

Use:

* Reporting Read Models
* server-side Excel generation
* HTML/CSS based PDF generation

## Deployment

Initial production target:

* Linux
* Docker Compose

---

# 5. Technologies Intentionally Excluded From Version 1

Do not introduce the following without a measured requirement and explicit approval:

* Microservices
* Kubernetes
* Redis
* Kafka
* RabbitMQ
* general-purpose Message Broker
* Data Warehouse
* BPMN Engine
* Event Sourcing
* CQRS everywhere
* generic Workflow Designer
* Elasticsearch
* Python as the primary ERP backend

Complexity is not quality.

Prefer the simplest design that preserves:

* correctness
* modularity
* performance
* security
* maintainability
* future scalability

---

# 6. Target Repository Structure

Keep the repository organized by architecture and business capability.

Preferred high-level structure:

```text
ERP Hadron Tavan/
│
├── AGENTS.md
├── README.md
├── docker-compose.yml
│
├── docs/
│   ├── ARCHITECTURE.md
│   ├── DATA_MODEL.md
│   ├── ROADMAP.md
│   ├── PERMISSIONS.md
│   ├── WORKFLOW.md
│   ├── TESTING.md
│   ├── MIGRATION.md
│   ├── DEPLOYMENT.md
│   ├── AI_ARCHITECTURE.md
│   ├── ui/
│   ├── modules/
│   └── ADR/
│
├── src/
│   ├── backend/
│   ├── frontend/
│   └── worker/
│
├── tests/
│
├── scripts/
│
└── assets/
```

The exact internal .NET solution structure may evolve when the first implementation begins.

Do not create unnecessary projects or layers simply to match a textbook architecture.

---

# 7. Module Boundaries

Business modules must have explicit ownership.

Expected module candidates include:

* Organization
* Identity
* Parties
* Catalog
* Engineering / BOM
* Planning / MRP
* Inventory
* Procurement
* Trading
* Logistics
* Finance Integration
* Reporting

Each business module should own its:

* domain rules
* application use cases
* database schema
* migrations
* API contracts
* permissions
* tests

A module must not directly modify another module's database tables.

Cross-module operations should use explicit:

* application contracts
* commands
* queries
* domain events
* integration events

Do not create circular dependencies between modules.

---

# 8. Platform Services

The following are shared platform capabilities and should not be independently reimplemented inside each module:

* Authentication
* Authorization
* Permission Management
* Organization Context
* Audit
* Workflow Infrastructure
* Document Infrastructure
* Notifications
* Background Jobs
* Transactional Outbox
* Localization
* Date and Time
* Configuration
* Feature Flags
* Observability
* AI Gateway

Keep platform infrastructure small and focused.

Do not place arbitrary business logic in a generic `Core`, `Common`, or `Utils` module.

---

# 9. Database Principles

PostgreSQL is the primary System of Record.

Do not reproduce Google Sheets as database tables.

Avoid:

* giant generic tables
* EAV models
* numbered spreadsheet-style columns
* duplicated master data
* excessive JSON
* business logic stored only in frontend code
* relationships based on row numbers

Use proper:

* primary keys
* foreign keys
* unique constraints
* check constraints
* indexes
* transactions
* decimal types
* date/time types

Use JSONB only for genuinely variable metadata.

---

# 10. Identity and Business Numbers

Separate technical identity from business-readable codes.

Examples:

A Product may have:

* immutable technical ID
* editable Product Code
* Product Name

A Purchase Order may have:

* immutable technical ID
* business PO Number

Never use:

* row number
* array position
* grid position
* display order

as permanent identity.

---

# 11. Product, Component and Party Modeling

Avoid unnecessary duplication.

Supplier and Customer should generally be modeled as roles of a shared Party model when appropriate.

Product and Component may share common Item concepts where beneficial.

However:

* BOM
* Revision
* Engineering relationships
* inventory behavior

must retain clear domain meaning.

Do not over-generalize merely to reduce table count.

---

# 12. Date and Time

Primary business timezone:

`Asia/Tehran`

Canonical computational dates should use standard Gregorian date/time types.

Use UTC for timestamps where appropriate.

Use database `date` for business dates without time.

Jalali dates are primarily:

* user input
* user display
* reporting presentation

Do not use Persian date strings as the canonical database representation when an equivalent real date exists.

Date conversion logic must be centralized.

Do not implement different Jalali conversion algorithms in multiple modules.

---

# 13. Money and Quantity

Never use floating point for:

* money
* exchange rates requiring precision
* inventory quantities requiring exact decimals
* BOM usage quantities

Use decimal/numeric precision explicitly.

Every monetary amount must have an explicit currency.

Never silently overwrite the original transaction currency with a converted value.

Quantities should have a Unit of Measure where applicable.

---

# 14. Transactions

Important business operations must be transactional.

Examples:

* Purchase Order creation
* approval
* Warehouse Receipt
* Stock Movement
* Inventory Adjustment
* financial posting
* status transition

An exception must never leave a partially completed business operation.

For operations that trigger asynchronous work:

**Business Transaction + Outbox Event**

must be persisted atomically.

---

# 15. Workflow

Do not build a large generic BPM system in the first version.

Create a small reusable Workflow Kernel supporting:

* State
* Transition
* Assignment
* Approval
* Deadline
* Reminder
* Comment
* Attachment
* History

Workflow does not replace domain rules.

For example:

A transition from:

`Draft → Approved`

does not automatically mean a Purchase Order is valid.

The Purchase Order domain must still validate required business rules.

---

# 16. Audit

Audit is mandatory for important business operations.

Record as appropriate:

* Actor
* Timestamp
* Entity Type
* Entity ID
* Action
* Field Changes
* Previous Value
* New Value
* Correlation ID
* Source
* Reason

Possible sources:

* UI
* API
* Import
* Background Job
* Automation
* AI Agent

Never silently destroy important business history.

Do not write secrets, passwords or tokens into audit records.

Audit is not a substitute for backups.

---

# 17. Authentication and Authorization

Authentication identifies the user.

Authorization determines what the user may do.

Backend authorization is mandatory.

Hiding UI controls is not security.

Use permissions such as:

```text
procurement.purchase_order.view
procurement.purchase_order.create
procurement.purchase_order.approve
inventory.receipt.post
reporting.finance.export
```

Data access may additionally be scoped by:

* company
* department
* site
* warehouse
* project
* owner
* approval amount

Approval and Edit permissions must remain separate where appropriate.

Support Maker-Checker / Segregation of Duties for sensitive processes.

---

# 18. Performance Is a Core Requirement

The previous Google Sheets systems became limited partly because too much data and too many calculations were repeatedly processed.

Do not reproduce those patterns.

Avoid:

* N+1 queries
* full-table loading
* row-by-row database operations
* unnecessary repeated calculations
* unnecessary API calls
* client-side filtering of huge datasets
* synchronous generation of very large exports
* unbounded queries
* full-system recalculation after small edits

Prefer:

* indexed queries
* server-side filtering
* pagination
* projections
* batch operations
* efficient joins
* read models
* background exports

Do not add caching infrastructure before measuring a real problem.

Performance improvements should be measured.

---

# 19. Reporting

Reporting is a first-class requirement.

Support:

* operational reports
* management dashboards
* filtering
* sorting
* search
* drill-down
* saved views
* Excel export
* PDF export

Do not distort transactional models merely to simplify one report.

Use dedicated reporting read models when appropriate.

Important KPIs should eventually define:

* calculation
* source
* owner
* refresh policy

Large exports should run as background jobs.

---

# 20. AI-Ready Architecture

The platform must be designed so AI Agents can be added later without redesigning core business logic.

AI Agents must interact through controlled:

* APIs
* application services
* tool contracts

AI must not have unrestricted direct database access.

Separate:

* Read
* Recommend
* Execute

A recommendation is not automatically a transaction.

High-impact actions should initially require human approval.

Examples:

* Purchase Order approval
* payments
* inventory adjustment
* supplier communication
* financial posting

AI actions must use normal authorization rules.

Record enough information to trace important AI operations.

---

# 21. Automation

Business automation should use durable background processing.

Expected pattern:

```text
Business Action
→ Database Transaction
→ Outbox Event
→ Worker
→ Automation / Notification / Integration
```

Background jobs should support:

* Idempotency
* Retry
* Backoff
* Timeout
* Correlation ID
* status tracking
* failure logging

Do not implement critical automation only inside the frontend.

---

# 22. API Design

Frontend, integrations and future AI Agents should use application APIs.

Prefer use-case-oriented endpoints or commands.

Good concepts:

* ApprovePurchaseOrder
* PostWarehouseReceipt
* GetStockPosition
* CalculatePurchaseRecommendation

Avoid exposing unrestricted generic CRUD operations as the main application model.

Business validation belongs in backend application/domain logic.

React must not become the source of truth for business rules.

---

# 23. First Strategic Vertical Slice

Do not begin by building a huge empty ERP shell.

The first major end-to-end business slice is:

```text
Forecast
+
BOM
+
Inventory
↓
Shortage Calculation
↓
Purchase Recommendation
↓
Approval
↓
Purchase Order
```

This slice should prove the architecture and eventually exercise:

* Product Master
* Component Master
* Supplier Master
* BOM
* Forecast
* Inventory
* MRP
* recommendation logic
* permissions
* workflow
* audit
* reporting
* Purchase Order creation

Before replacing the existing MRP system, run both systems in parallel and compare results using approved datasets.

---

# 24. Migration From Existing Systems

Existing sources include:

* Excel
* Google Sheets
* Google Apps Script
* HTML tools
* MRP sheets
* Trading systems
* financial tools

Migration must be gradual.

Use a Strangler Migration approach.

For each domain:

1. inspect the current source
2. document business rules
3. identify the current System of Record
4. create approved sample datasets
5. implement equivalent logic
6. compare results
7. reconcile differences
8. run in parallel
9. obtain approval
10. migrate ownership
11. make the old system read-only

Do not maintain uncontrolled permanent dual-write between old and new systems.

At any moment, every critical data category should have one authoritative source.

---

# 25. Golden Dataset and MRP Correctness

MRP calculations affect purchasing and inventory and therefore require stronger validation than normal CRUD features.

For important migrated calculations, create Golden Datasets.

A Golden Dataset must contain:

* known input
* approved expected output
* explanation where appropriate
* tolerance if decimals require it

Codex must not claim an MRP calculation is correct simply because:

* code compiles
* tests execute
* output looks plausible

Compare results against approved business scenarios.

---

# 26. Testing Requirements

Add tests for meaningful behavior changes.

Use as appropriate:

* Unit Tests
* Integration Tests
* API Tests
* Authorization Tests
* Migration Tests
* Regression Tests
* Golden Tests

Always test relevant:

* success paths
* validation failures
* authorization boundaries
* financial edge cases
* inventory edge cases
* duplicate prevention
* transaction rollback

Tests must be deterministic.

Never require production credentials or production services for normal test execution.

---

# 27. Security and Secrets

Never commit:

* passwords
* API keys
* access tokens
* database credentials
* production secrets
* customer confidential data
* real `.env` files

Provide sanitized examples such as:

`.env.example`

Validate required configuration at startup.

Use least privilege.

Development and Codex must not receive unrestricted Production access.

---

# 28. Environment Separation

Maintain:

* Development
* Automated Test / CI
* Staging / UAT
* Production

Codex should normally operate only against:

* Development
* Test

Production changes require explicit approval and controlled deployment.

Do not automatically run destructive migrations in Production.

---

# 29. Code Quality

Prefer:

* explicit code
* strong typing
* small focused classes
* clear naming
* low coupling
* high cohesion
* predictable patterns

Avoid:

* giant service classes
* giant controllers
* utility dumping grounds
* circular dependencies
* duplicate business logic
* hidden global state
* silent exception handling
* premature abstractions
* excessive generic frameworks

Do not create an abstraction until there is a real need for it.

---

# 30. Naming

Use clear English technical identifiers.

Use:

* `PascalCase` for C# types
* `camelCase` for JavaScript/TypeScript variables and functions
* established .NET naming conventions
* consistent database naming conventions

Avoid unexplained abbreviations.

Business terminology must remain consistent across:

* backend
* frontend
* database
* documentation
* reports

When terminology is ambiguous, clarify it before spreading different names across the codebase.

---

# 31. Documentation

Important project knowledge must live in the repository, not only inside Codex or ChatGPT conversations.

Maintain as needed:

```text
docs/ARCHITECTURE.md
docs/DATA_MODEL.md
docs/ROADMAP.md
docs/PERMISSIONS.md
docs/WORKFLOW.md
docs/TESTING.md
docs/MIGRATION.md
docs/DEPLOYMENT.md
docs/AI_ARCHITECTURE.md
docs/ui/HADRON-ERP-UX-ARCHITECTURE.md
docs/modules/
docs/ADR/
```

Use Architecture Decision Records for major decisions.

Do not rely on conversation history as the only documentation.

---

# 32. Codex Context Efficiency

Use context carefully.

Do not reread the entire repository for every task.

For each task:

1. read this `AGENTS.md`
2. inspect relevant project structure
3. read relevant module documentation
4. search for relevant symbols
5. open only required files
6. inspect related tests
7. make the smallest correct change
8. run focused tests
9. run broader tests only when justified

Do not print large unchanged files in responses.

Do not repeatedly summarize the entire architecture.

Report primarily:

* files inspected
* files changed
* important decisions
* tests executed
* results
* unresolved risks

---

# 33. Mandatory Planning Before Important Changes

Before implementing any meaningful change, understand:

* business scenario
* affected module
* source of truth
* affected entities
* database impact
* permissions
* workflow impact
* audit requirements
* reporting impact
* tests

For the following changes, provide a plan and **STOP for explicit approval before implementation**:

* architecture changes
* new major modules
* database redesign
* destructive migrations
* cross-module dependencies
* changes to inventory logic
* changes to MRP formulas
* changes to purchase recommendation logic
* changes to financial calculations
* authentication or authorization architecture
* removal of legacy behavior
* deletion of historical data

For normal implementation tasks that the project owner has already explicitly approved, execute the agreed scope without asking repeated minor questions.

---

# 34. Do Not Invent Business Rules

Never invent missing business rules when they materially affect:

* MRP
* inventory
* purchasing
* finance
* approval
* supplier obligations
* security
* audit
* data ownership

If a missing rule could materially change the business result:

**STOP and ask.**

For minor technical details that do not alter business behavior, use sound engineering judgment.

---

# 35. Working With Legacy Google Sheets

When reviewing legacy Excel or Google Sheets:

Do not assume every existing:

* sheet
* formula
* script
* HTML form
* column

belongs in the new system.

Classify legacy behavior into:

* business rule
* data
* UI behavior
* reporting
* temporary workaround
* obsolete logic
* test code

Preserve business meaning, not accidental spreadsheet structure.

Never modify source legacy files unless explicitly instructed.

---

# 36. Build and Development Commands

The repository is initially a scaffold.

When project tooling is created, expose a small predictable command set in `README.md`.

Target commands should eventually make common operations obvious, for example:

```text
start development environment
build
run tests
run linting
apply development migrations
start/stop containers
```

Do not add placeholder commands that report success without performing the stated operation.

Prefer one documented entry point for local startup.

---

# 37. Git and Change Discipline

Keep changes narrowly scoped.

Do not mix:

* refactoring
* architecture changes
* database redesign
* new features

in one uncontrolled change set.

Commit messages should be concise and meaningful.

Examples:

```text
feat: add purchase recommendation engine
fix: prevent duplicate warehouse receipt
refactor: isolate BOM calculation service
test: add shared-component MRP scenarios
```

Before destructive or difficult-to-reverse changes, ensure a rollback path exists.

---

# 38. Definition of Done

A feature is not complete merely because the UI works.

Depending on the feature, completion may require:

* domain rule implemented
* backend validation
* authorization
* database migration
* transaction safety
* audit
* tests
* error handling
* performance review
* UI states
* documentation
* no unrelated regression

For important business calculations, validated expected results are mandatory.

---

# 39. Current Project Priority

The immediate objective is to establish the platform foundation correctly and then implement the first useful vertical slice.

Do not start unrelated ERP modules prematurely.

Primary functional direction:

```text
Master Data
↓
Forecast + BOM + Inventory
↓
MRP / Shortage
↓
Purchase Recommendation
↓
Human Approval
↓
Purchase Order
↓
Receipt
```

Keep future expansion possible, but optimize current implementation for real Hadron Tavan requirements rather than hypothetical enterprise complexity.

---

# 40. Response Format for Codex

For analysis or planning tasks, keep responses concise and structured.

Use this format when appropriate:

## Understanding

What the business requirement means.

## Findings

Relevant code, data or architecture findings.

## Recommendation

Best technical approach.

## Risks

Only material risks.

## Plan

Small implementation steps.

## Approval Required

Clearly state whether implementation should wait.

For implementation tasks, report:

## Changed

Files and behavior changed.

## Tested

Commands/tests executed.

## Result

What works.

## Remaining

Known risks or next step.

Do not repeat the entire project architecture in every response.

# Brand and Visual Identity

Hadron Tavan has an official Brand Identity Guideline.

The canonical authority hierarchy for Hadron ERP is:

1. Business and domain behavior: canonical module specifications, `docs/modules/mrp/DECISIONS.md`, and other locked Decisions.
2. Enterprise UX, navigation, shell, page patterns, and interaction behavior: `docs/ui/HADRON-ERP-UX-ARCHITECTURE.md`.
3. Digital visual UI, brand expression, design tokens, colors, logo, typography, spacing, and components: `docs/brand/HADRON-DIGITAL-DESIGN-SYSTEM-v1.0.pdf`.

`docs/brand/Hadron-Brand-Guidelines.pdf` remains the underlying corporate brand source incorporated by the Digital Design System. For ERP digital implementation, follow the Digital Design System and its governance rules.

Before any UI/UX analysis, implementation, audit, or redesign, read the relevant canonical business specification and Decisions, then the UX Architecture, the Digital Design System, and `docs/brand/UI-BRAND-RULES.md`.

The product title is exactly `Hadron ERP System`. MRP is a workspace/business area inside Hadron ERP System. Package identifiers such as `MRP-01` through `MRP-14` are development/governance references and must never become user-facing navigation.

SAP Fiori patterns govern enterprise UX behavior and interaction patterns only. Do not copy SAP visual branding, colors, or visual identity; all visual expression remains Hadron.

The underlying corporate brand reference is:

`docs/brand/Hadron-Brand-Guidelines.pdf`

All user-facing frontend design must respect this document.

This requirement applies to:

- application shell
- login page
- navigation
- dashboards
- forms
- tables
- dialogs
- reports
- PDF outputs
- printable documents
- empty states
- notifications
- management dashboards

Do not invent an unrelated visual identity for the ERP.

The application must visually belong to Hadron Tavan.

However, do not blindly reproduce print layouts from the Brand Guideline inside software interfaces.

Translate the brand principles into a modern, fast and functional enterprise web UI.

Priority order:

1. usability
2. clarity
3. speed
4. consistency
5. accessibility
6. Hadron brand identity
7. visual decoration

The Hadron Digital Design System is the source of truth for ERP digital implementation of:

- logo usage
- logo clear space
- brand colors
- secondary colors
- typography
- visual hierarchy
- spacing philosophy
- layout character
- visual tone
- use of patterns
- brand personality

It preserves the underlying official identity rules from the corporate Brand Guideline according to its own governance section.

Key visual characteristics of the Hadron interface should be:

- professional
- calm
- precise
- engineered
- modern
- minimal
- trustworthy
- controlled
- clean
- low-noise

Avoid:

- visually crowded screens
- unnecessary gradients
- excessive shadows
- decorative glassmorphism
- random colors
- overly rounded consumer-app styling
- excessive animation
- flashy dashboards
- unnecessary illustrations
- inconsistent spacing
- fonts outside the approved system without justification

The ERP is an operational business application, not a marketing website.

Brand elements must support usability rather than compete with business data.

For Persian UI, follow the approved Persian typography from the Brand Guideline.

For English UI, follow the approved English typography when technically and legally available.

Do not download, substitute or redistribute proprietary font files without confirming licensing and availability.

Official Hadron logos and approved frontend brand assets are located under:

`src/frontend/src/assets/brand/`

Do not redraw, recreate or approximate the Hadron logo with text or CSS.

Use the official asset.

Before creating or changing the production UI design system, review:

- `docs/ui/HADRON-ERP-UX-ARCHITECTURE.md`
- `docs/brand/HADRON-DIGITAL-DESIGN-SYSTEM-v1.0.pdf`
- `docs/brand/UI-BRAND-RULES.md`
- `docs/brand/Hadron-Brand-Guidelines.pdf` where the Digital Design System identifies an underlying official brand rule

and update, when required:

`docs/brand/UI-BRAND-RULES.md`

The `UI-BRAND-RULES.md` file should contain the web-specific interpretation of the Brand Guideline so that routine frontend tasks do not need to repeatedly load the full PDF.

If there is a conflict between `UI-BRAND-RULES.md` and the Hadron Digital Design System PDF, the Digital Design System PDF is authoritative. UX structure and interaction conflicts are resolved by the UX Architecture, while neither UX nor visual documents may override canonical business rules.

Changes to global:

- colors
- typography
- spacing scale
- logo rules
- design tokens
- application shell

require project-owner approval.
