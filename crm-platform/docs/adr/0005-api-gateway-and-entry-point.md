# ADR 0005 — API Gateway and External Entry Point

**Status:** Accepted  
**Date:** 2026-04-04  
**Deciders:** Technical Director  
**Supersedes:** —  
**Superseded by:** —

---

## Context

The platform has 10+ backend microservices running on Azure Container Apps. External clients (browser frontends, mobile apps, third-party integrations) need a single, consistent, secure entry point. Without a governed API gateway:

- Each service would need to independently implement rate limiting, JWT validation, and CORS
- There is no single place to enforce tenant-level throttling or API versioning
- Backend service URLs would be exposed to clients, creating tight coupling
- Observability across all inbound traffic would be fragmented

Additionally, ADR 0002 created an exception: "APIM → backend services uses HTTP." This ADR governs everything upstream of that boundary.

The following must be resolved:

1. What is the single external entry point and what does it own?
2. Do the frontend applications talk directly to APIM, or via a Backend for Frontend (BFF)?
3. How is API versioning managed?
4. How does APIM route traffic to the correct Container App service?
5. How does the gateway handle the dual deployment model (SaaS vs client-hosted)?

---

## Options Considered

### Option A — Azure API Management (APIM) as sole gateway
All external traffic enters via APIM. Services are internal only (no public ingress on Container Apps). APIM owns: routing, JWT validation (Layer 1 per ADR 0004), rate limiting, versioning, CORS. This is the standard enterprise pattern on Azure.

### Option B — Azure Container Apps built-in ingress only
No APIM. Each service exposed individually via Container Apps ingress. Simple, low cost. Rejected — no centralised policy, no tenant-level throttling, services exposed directly to internet.

### Option C — APIM + Backend for Frontend (BFF) per portal
APIM handles external policy. A dedicated BFF service sits between each frontend and the microservices, aggregating calls and shaping responses for the UI. Adds a layer but solves the chatty API problem for complex UIs.

### Option D — Azure Front Door + APIM
Front Door provides global CDN and WAF in front of APIM. Adds global load balancing and DDoS protection. Considered for R2 when multi-region is required. Not adopted in R1 — over-engineered for a single-region initial deployment.

---

## Decision

**Adopt Option C for the staff portal (APIM + BFF). Adopt Option A for the customer portal and third-party API consumers.**

Azure Front Door (Option D) is deferred to R2 as the platform scales to multi-region.

---

## Detail

### Traffic Architecture

```
                    ┌─────────────────────────────────────┐
Internet            │         Azure API Management         │
traffic      ───►   │  • JWT validation (Layer 1)          │
                    │  • Tenant throttling                 │
                    │  • API versioning                    │
                    │  • CORS policy                       │
                    │  • Request logging (no PII)          │
                    └──────────────┬──────────────────────┘
                                   │
              ┌────────────────────┼───────────────────────┐
              │                   │                        │
              ▼                   ▼                        ▼
     ┌─────────────┐     ┌──────────────┐        ┌──────────────────┐
     │  Staff BFF   │     │ Customer     │        │  Public API      │
     │  (per ADR    │     │ Portal APIs  │        │  (3rd party      │
     │   below)     │     │ (direct)     │        │   integrations)  │
     └──────┬───────┘     └──────┬───────┘        └────────┬─────────┘
            │                   │                          │
            └───────────────────┴──────────────────────────┘
                                │
                    ┌───────────▼──────────────┐
                    │   Container Apps (VNet)  │
                    │   Internal ingress only  │
                    │   (no public endpoints)  │
                    └──────────────────────────┘
```

### Azure API Management — Responsibilities

APIM is the **only** resource with a public IP / DNS endpoint for backend traffic. All Container Apps services use **internal ingress only** — they are not reachable from the internet directly.

APIM owns:

| Concern | Implementation |
|---------|----------------|
| JWT validation (Layer 1) | `validate-jwt` policy — validates signature, expiry, issuer (per ADR 0004) |
| Tenant throttling | `rate-limit-by-key` policy keyed on resolved `TenantId` header |
| API versioning | URL path versioning: `/api/v1/`, `/api/v2/` |
| CORS | Centralised CORS policy — no per-service CORS configuration permitted |
| Request tracing | `set-header` injects `X-Correlation-Id` on every request |
| Backend routing | Named backends per service, resolved by URL path prefix |
| Claim forwarding | `set-header` extracts `tid` and `oid` from JWT, forwards as `X-Tenant-Oid` and `X-User-Oid` to backends |

APIM does **not** own:

- Business authorisation (role checks) — this is service middleware (ADR 0004)
- Request body transformation — services receive payloads unmodified
- Caching — services own their own caching strategy

### Staff Portal — Backend for Frontend (BFF)

The staff portal is a complex, multi-module UI. Without a BFF, the frontend would make 5–10 API calls to render a single screen (leads + activities + contacts + cases + pipeline). This creates chattiness and exposes the internal service decomposition to the UI.

The `staff-bff` service:

- Is a lightweight ASP.NET Core service deployed on Container Apps (internal + external ingress — external only reachable via APIM)
- Aggregates calls to multiple backend services for composite UI payloads
- Is the **only** service that makes cross-domain aggregation calls within a single HTTP request cycle (this is the exception to service-to-service HTTP prohibition in ADR 0002 — BFF → backend is UI aggregation, not business logic coupling)
- Performs no business logic — it shapes, aggregates, and returns data only
- Has its own CLAUDE.md extending root rules with BFF-specific constraints

One BFF per deployment model (SaaS BFF, client-hosted BFF deployment). They share the same codebase.

### Customer Portal — Direct to APIM

The customer portal has simpler, narrower read/write patterns (view my cases, submit a request, view my account). A BFF is not warranted. The customer portal frontend calls APIM-proxied endpoints directly.

Customer portal services are a strict subset of the full service API — they are tagged in APIM with a `customer-portal` product that limits which operations are accessible.

### API Versioning

URL path versioning: `/api/v1/{service}/{resource}`

- All v1 endpoints are stable within R1
- Breaking changes require a new version prefix (`/api/v2/`)
- APIM routes by version prefix to the appropriate backend
- Old versions remain available for a minimum of 6 months after a new version is released (deprecation policy to be formalised in a runbook)
- The `api-version` header is supported as an alternative to path versioning for clients that cannot modify URL paths (third-party integrations)

### Routing Convention

| Path prefix | Routes to |
|------------|-----------|
| `/api/v1/sfa/` | `sfa-service` Container App |
| `/api/v1/css/` | `css-service` Container App |
| `/api/v1/marketing/` | `marketing-service` Container App |
| `/api/v1/analytics/` | `analytics-service` Container App |
| `/api/v1/identity/` | `identity-service` Container App |
| `/api/v1/platform/` | `platform-service` Container App (PlatformAdmin only) |
| `/bff/v1/` | `staff-bff` Container App |

### Dual Deployment Model

| Concern | SaaS (shared) | Client-hosted |
|---------|--------------|---------------|
| APIM instance | Shared platform APIM | Dedicated APIM in client subscription |
| Custom domain | `api.crmplatform.io` | `api.{client-domain}.com` (client configures) |
| JWT issuers configured | Platform Entra + Platform External ID | Client Entra + Client External ID (per ADR 0004) |
| Rate limiting | Per-tenant limits from platform plan | Configurable per client agreement |
| APIM tier | Standard v2 (shared) | Developer/Standard v2 depending on client size |

The Bicep module for APIM (`infra/modules/apim.bicep`) accepts parameters for issuer URLs and domain, making it deployable in both configurations from the same template.

---

## Consequences

### Positive
- Single policy enforcement point — JWT validation, CORS, throttling configured once
- Backend services have no public surface area — attack surface is minimised
- BFF solves the chattiness problem for the staff portal without coupling services
- API versioning managed centrally — clients are not broken by internal service changes
- APIM Bicep module is reusable across SaaS and client-hosted deployments

### Negative
- APIM adds latency (~2–5ms per request) — acceptable for a CRM workload
- BFF is an additional service to maintain and test
- APIM Developer/Standard tier has cost implications — must be factored into client-hosted pricing
- Local development requires either APIM emulation or direct service calls — see ADR 0008

---

## Not In Scope (R1)

- Azure Front Door (global CDN + WAF) — deferred to R2 multi-region rollout
- GraphQL API — REST only in R1
- WebSocket / SignalR for real-time UI updates — deferred to R2
