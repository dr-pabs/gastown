# ADR 0014 — Headless API Contract Standards

**Status:** Proposed  
**Date:** 2026-04-30  
**Deciders:** Technical Director  
**Supersedes:** —  
**Superseded by:** —

---

## Context

ADR 0013 resets the product toward a headless platform with one retained first-party UI (`staff-portal`) and customer/self-service capability delivered through APIs, events, and integrations.

The repository already exposes Minimal API services and Swagger in development, but the external contract layer is still inconsistent and under-governed:

- services register Swagger locally, but there is no repository-level contract publication standard
- endpoint shapes vary by service and by feature
- pagination conventions are implemented ad hoc inside handlers/endpoints
- error responses mix `ProblemDetails`, anonymous payloads, and direct `Results.*` calls
- idempotency exists for Service Bus consumers, but HTTP write-contract rules are not yet defined
- long-running workflows exist in Durable Functions and workers, but the HTTP contract for async operations is not standardised

Examples in the current tree:

- Swagger is enabled in development in multiple services, including `src/services/css-service/Program.cs` and `src/services/integration-service/Program.cs`
- `src/services/integration-service/Api/IntegrationEndpoints.cs` uses explicit `page`/`pageSize` query parameters and custom paged DTOs
- `src/services/css-service/Api/CssEndpoints.cs` now contains both internal/staff and customer/headless case routes, but contract shape and result handling are still service-local
- `src/services/css-service/Api/ResultHttpExtensions.cs` had to be added locally because the shared `ToHttpResult()` helper expected by handlers was missing from the shared template

The headless platform needs a contract model that is stable, versioned, externally consumable, and reusable across services.

---

## Options Considered

### Option A — Let each service define its own API conventions
This is fast locally but produces drift across DTOs, pagination, errors, and consumer expectations. Rejected.

### Option B — Centralise all external APIs behind a dedicated gateway/BFF layer first
This may become useful for some composite staff experiences, but it is too heavy as the primary answer for all external contracts. Rejected for R1.

### Option C — Standardise service-owned contracts and publish them consistently through APIM
Each bounded-context service remains the owner of its external contract, but the contract model is governed centrally and exposed through APIM products. This fits the current service topology and the headless direction.

---

## Decision

**Adopt Option C. External contracts remain service-owned, but all supported headless APIs must follow shared contract standards and be published as versioned APIM-backed products.**

The rebuilt contract layer will use the following rules:

1. **OpenAPI is a first-class build output** for every externally consumable service
2. **Public and internal endpoints are separated intentionally**
3. **RFC 7807 Problem Details** is the default error model
4. **Standard pagination/filtering rules** apply across external list endpoints
5. **HTTP idempotency rules** are defined explicitly for write operations
6. **Long-running operations** use a consistent `202 Accepted` + operation-status pattern
7. **Correlation and audit semantics** are part of the contract, not an implementation detail

---

## Detail

### Contract ownership

Each bounded-context service owns its own domain contract. APIM does not invent business payloads; it publishes, versions, and protects the contracts exposed by services.

Initial contract-priority services for the headless model are:

| Priority | Service | Reason |
|---|---|---|
| 1 | `css-service` | canonical customer/self-service case API |
| 1 | `integration-service` | third-party connectors, webhooks, job visibility |
| 2 | `identity-service` | tenant/auth support contracts that must be consumable beyond browser-only assumptions |
| 2 | `platform-admin-service` | operational/admin flows being moved away from page-driven settings |
| 3 | `sfa-service`, `marketing-service`, `analytics-service`, `notification-service` | exposed after the initial headless customer/integration seams are stable |

### OpenAPI publication

Every externally supported service must:

- generate an OpenAPI document in CI
- publish that document as an artifact
- expose a stable APIM-importable document for the supported public surface
- exclude internal-only endpoints from the public contract

Repository implications:

- keep local Swagger UI for development
- add CI export/publication for supported services
- formalise endpoint visibility using route groups, tags, or explicit exclusion rather than leaving public/internal mixed by accident

### Public vs internal boundary

Services may expose both:

- **public/headless endpoints** — intended for APIM products and external consumers
- **internal endpoints** — orchestration, service-to-service, platform operations

Internal endpoints must not leak into the public headless contract by default. Existing examples like hidden endpoints in `identity-service` and explicit internal route groupings in `css-service` should become the norm rather than service-local exceptions.

### API versioning

External APIs are versioned at the APIM contract boundary. The initial standard remains path-based versioning:

```text
/api/v1/css/...
/api/v1/integrations/...
```

Rules:

- breaking changes require a new major version path
- additive changes remain within the current version
- APIM products publish only supported versions
- deprecated versions remain available for a bounded support window defined outside this ADR

### Error model

Externally supported APIs use **Problem Details** (`application/problem+json`) for all non-2xx outcomes.

This replaces ad hoc responses such as:

- anonymous `{ detail = ... }` payloads
- direct `Results.NotFound()` with no body where a consumer-facing error document is more appropriate

Required problem extensions for supported APIs:

- `traceId`
- `correlationId`
- `errorCode` where the service has a stable domain/application error code

### Pagination and filtering

All external list endpoints use the same request/response contract shape:

- `page` — 1-based
- `pageSize` — clamped to service/product limits
- optional resource-specific filters as query parameters
- response envelope containing:
  - `items`
  - `page`
  - `pageSize`
  - `total`

This matches existing patterns already visible in `integration-service` and the new customer case routes, and should be formalised into shared DTO/convention support instead of repeated endpoint-local code.

### Idempotency

Idempotency is already treated as mandatory for asynchronous consumers via `IIdempotencyStore`. The same principle is now extended to externally supported HTTP writes.

Rules:

- side-effecting public `POST`/`PATCH` operations that may be retried by clients must support an idempotency key contract
- idempotency storage may remain service-local
- duplicate idempotent requests must return the original outcome or a defined safe equivalent

This is especially relevant for:

- case creation / case comment submission
- replay or dispatch operations
- connector or job-trigger endpoints exposed to automation clients

### Long-running operations

Where completion is asynchronous, the HTTP contract must not pretend the operation is immediate.

Standard pattern:

1. client submits request
2. service returns `202 Accepted`
3. response includes an operation resource or status URL
4. client polls or receives event/webhook completion

This applies to orchestration-backed work, integration dispatch/replay, and future AI/job-style operations.

### Correlation and audit semantics

Every supported external API must preserve correlation identifiers across APIM, service logs, and downstream events.

Minimum standard:

- accept or emit `X-Correlation-Id`
- include correlation data in Problem Details extensions and operation resources where useful
- ensure externally visible audit identifiers do not depend on UI-only behavior

### Shared implementation direction

To reduce repeated service-local fixes, the contract layer should introduce shared primitives in the service template or a shared library for:

- result-to-HTTP mapping
- Problem Details shaping
- paged response DTOs
- idempotency-key handling
- operation-status DTOs

The current `css-service` local stopgap for `ToHttpResult()` should be treated as evidence that these primitives belong in shared infrastructure.

---

## Consequences

### Positive
- Makes headless APIs publishable and consumable without relying on portal behavior
- Reduces service-by-service contract drift
- Gives APIM a stable import/version target
- Creates a clean foundation for partner assets, SDKs, and contract tests

### Negative
- Requires follow-on shared-library/template work before all services can conform cleanly
- Will force cleanup of existing endpoint and DTO inconsistencies
- Raises the quality bar for services that previously relied on UI coupling to hide contract gaps
