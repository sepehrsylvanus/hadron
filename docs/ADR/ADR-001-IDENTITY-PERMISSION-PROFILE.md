# ADR-001 — Central Identity, Permission and Personnel Profile Foundation

## Status

Owner approved and implemented on 2026-08-26.

## Context

The completed MRP v1 application required central authentication, backend authorization, module/action permissions and a real user profile in the global shell. `organization.internal_people` remains a Supplier Owner master and does not serve as a login account.

## Decision

- Use ASP.NET Core Identity with UUID users and secure Cookie sessions.
- Prohibit public registration; System Administrators create accounts and temporary passwords.
- Require password change on first login.
- Use RBAC, direct Grant/Deny overrides, typed Data Scope and existing business policies.
- Permit a System Administrator to grant complete System Administrator authority to another user.
- Protect the final active System Administrator.
- Require a personnel profile with First Name, Last Name, Email, Cellphone, Organizational Unit and Personnel Photo.
- Keep employee Organizational Unit separate from Product `BUSINESS_UNIT` and keep the login profile separate from Supplier `InternalPerson`.
- Store private validated personnel photos through the S3-compatible abstraction using MinIO initially.
- Require explicit permission registration for every current and future ERP endpoint, route and action; missing metadata fails closed.
- Keep backend authorization authoritative. Permission-aware UI is a usability layer only.

The complete permission catalog and invariants are defined in `docs/PERMISSIONS.md`.

## Consequences

- All existing business endpoints and frontend actions require explicit permission mapping.
- Identity, Organization Unit and storage persistence require forward migrations and configuration.
- The application shell gains authenticated profile, password-change and logout behavior.
- Security audit and session revocation become platform responsibilities.
- Existing MRP, Inventory, Procurement and Analytics business rules remain unchanged.
