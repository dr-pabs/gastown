# ADR 0004 — Authentication and Authorisation Architecture

**Status:** Accepted  
**Date:** 2026-04-04  
**Deciders:** Technical Director  
**Supersedes:** —  
**Superseded by:** —

---

## Context

The platform has two distinct user populations with different identity requirements:

- **Staff users** — employees of tenant organisations (sales reps, agents, managers, admins). Authenticated via their corporate identity.
- **Customer users** — end customers of tenant organisations. Authenticated via a consumer-friendly identity flow.

Both populations access the same backend microservices, but with different trust levels, token issuers, and authorisation scopes. The platform also operates in two deployment models (SaaS shared and client-hosted), which affects how identity tenants are configured.

The following concerns must be resolved:

1. Which identity provider(s) issue tokens, and for which user population?
2. How does a token's `tid` claim map to a platform `TenantId`?
3. Where is JWT validation performed — APIM, middleware, or both?
4. How is Role-Based Access Control (RBAC) modelled and enforced?
5. How does client-hosted deployment differ from SaaS in identity terms?

---

## Options Considered

### Option A — Single Entra tenant for all users
One Azure Active Directory tenant. Staff users and customers coexist. Simple token validation, but customers in an AAD tenant is a poor UX and a security model mismatch. Rejected.

### Option B — Entra ID (staff) + Entra External ID (customers), separate issuers
Staff authenticate via the platform's Entra ID tenant (or their own, federated in). Customers authenticate via Entra External ID (CIAM), which is purpose-built for consumer identity. Two issuers, two token validation paths. This is the correct separation of concerns.

### Option C — Third-party IdP (Auth0, Okta)
Introduces a vendor not in the Azure ecosystem, adding cost, operational complexity, and a trust boundary outside Azure. Rejected — platform is Azure-only by design (see ADR 0003).

---

## Decision

**Adopt Option B: Entra ID for staff, Entra External ID for customers, with APIM as the first validation layer and per-service middleware as the second.**

---

## Detail

### Identity Providers

| User Population | Identity Provider | Token Issuer |
|----------------|-------------------|--------------|
| Staff (agents, sales reps, managers, admins) | Azure Entra ID | `https://login.microsoftonline.com/{platform-tenant-id}/v2.0` |
| Customers (end users of tenant orgs) | Azure Entra External ID | `https://{platform-b2c-tenant}.ciamlogin.com/{platform-b2c-tenant}.onmicrosoft.com/v2.0` |

### Token Validation — Two Layers

**Layer 1 — Azure API Management (APIM)**

APIM is the single external entry point for all traffic. It performs:

- JWT signature validation against the issuer's JWKS endpoint
- Expiry (`exp`) and not-before (`nbf`) claim validation
- Rejection of tokens with unknown or untrusted issuers (HTTP 401)
- Extraction and forwarding of `tid` and `oid` claims as request headers to backend services

APIM does **not** perform business authorisation — it is a gateway, not an authorisation server.

**Layer 2 — Per-service ASP.NET Core middleware**

Every microservice validates:

- The `Authorization: Bearer` header is present and structurally valid
- The token issuer matches the expected value for the service's user population
- The `tid` claim resolves to a known, active platform tenant (via `ITenantContext`)
- The token has not been tampered with (signature re-validation at service level)

Middleware runs **before** any controller action. Controllers must never access `HttpContext.User` before middleware has set `ITenantContext`. This is enforced by the `[RequiresTenantContext]` attribute on all controllers.

### Tenant ID Mapping

The JWT `tid` claim is **not** used directly as the platform `TenantId`. The mapping is:

```
JWT tid claim → Tenant Registry lookup → Platform TenantId (Guid)
```

The Tenant Registry is a table in the platform database (`dbo.Tenants`) maintained by the `identity` service. It stores:

| Column | Type | Notes |
|--------|------|-------|
| `TenantId` | `uniqueidentifier` | Platform-internal GUID — used in all EF Core query filters |
| `EntraDirectoryTenantId` | `nvarchar(36)` | The AAD `tid` claim value for staff users |
| `ExternalIdTenantId` | `nvarchar(36)` | The Entra External ID tenant identifier for customer users |
| `Status` | `nvarchar(20)` | `Active`, `Suspended`, `Deprovisioned` |
| `PlanId` | `uniqueidentifier` | FK to billing plan |

The `ITenantContext` service resolves this mapping on every request and caches it for the lifetime of the request. A `tid` that does not resolve to an `Active` tenant returns HTTP 403.

### Role-Based Access Control

Roles are stored in the platform database, not in the JWT, to allow runtime role changes without token re-issuance.

| Concept | Location | Notes |
|---------|----------|-------|
| User identity | JWT (`oid` claim = user's object ID in Entra) | Immutable per user |
| User roles | `dbo.UserRoles` table, scoped by `TenantId` | Mutable — changes take effect on next request |
| Permission evaluation | `IAuthorizationService` in ASP.NET Core | Policy-based, evaluated in middleware |

Built-in platform roles:

| Role | Scope | Description |
|------|-------|-------------|
| `TenantAdmin` | Tenant | Full access to tenant configuration and all modules |
| `SalesRep` | Tenant | SFA module — own records only by default |
| `SalesManager` | Tenant | SFA module — all team records |
| `SupportAgent` | Tenant | CS&S module — assigned cases |
| `SupportManager` | Tenant | CS&S module — all cases, SLA dashboard |
| `MarketingUser` | Tenant | Marketing module — campaigns and journeys |
| `AnalyticsViewer` | Tenant | Read-only access to Analytics module |
| `PlatformAdmin` | Platform | Cross-tenant admin — platform operators only |

`PlatformAdmin` is a platform-level role stored separately from tenant roles. It may only be assigned by the platform operator (not by any tenant admin). Services must explicitly check for `PlatformAdmin` before allowing cross-tenant operations.

### Client-Hosted Deployment Identity

In client-hosted deployments, the client brings their own Azure subscription. Identity is handled as follows:

- **Staff users:** The client's own Entra ID tenant issues tokens. The platform's APIM instance is configured with the client's `tid` value as the trusted issuer. The client is responsible for managing their Entra ID users and groups.
- **Customer users:** The platform provisions a dedicated Entra External ID tenant within the client's Azure subscription. This keeps customer identity data within the client's compliance boundary.
- **Tenant Registry:** A single-row `Tenants` table entry is created during client-hosted provisioning, mapping the client's Entra `tid` to the platform `TenantId`.

### Token Flow (Staff Portal, SaaS)

```
Browser
  → MSAL.js acquires token from Entra ID
  → HTTP request to APIM with Authorization: Bearer {token}
  → APIM validates signature, extracts tid + oid, forwards to service
  → Service middleware resolves TenantId from tid via ITenantContext
  → Service middleware loads user roles from dbo.UserRoles
  → Controller action executes with full tenant and user context
```

### Token Flow (Customer Portal, SaaS)

```
Browser
  → MSAL.js acquires token from Entra External ID
  → HTTP request to APIM with Authorization: Bearer {token}
  → APIM validates against Entra External ID JWKS endpoint
  → Service middleware resolves TenantId from ExternalIdTenantId claim
  → Controller action executes — customer scope only
```

---

## Consequences

### Positive
- Clear separation between staff and customer identity with appropriate trust levels
- Defence-in-depth validation (APIM + middleware) — no single point of bypass
- Roles are runtime-configurable without token changes
- Client-hosted deployments have a clear, documented identity configuration
- Entra External ID is purpose-built for customer CIAM — good UX, MFA, social login support

### Negative
- Two JWKS validation paths in APIM require careful policy management
- Tenant Registry lookup on every request — must be cached aggressively (in-memory, short TTL)
- Role model complexity increases as modules grow — must be governed via the role table, not ad-hoc checks in code

---

## Compliance Notes

- The mandatory tenant isolation test (see root CLAUDE.md) must include a case where a valid JWT from Tenant B is used to call a Tenant A endpoint — this must return HTTP 403, never Tenant A data.
- `PlatformAdmin` endpoints must have a separate test suite confirming they are inaccessible to any tenant-scoped JWT.
- No user PII (name, email, phone) from the JWT may be logged — only `oid` and the resolved platform `TenantId` are permissible in log entries.
