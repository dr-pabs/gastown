# ADR 0015 — Headless Integration Surfaces

**Status:** Proposed  
**Date:** 2026-04-30  
**Deciders:** Technical Director  
**Supersedes:** —  
**Superseded by:** —

---

## Context

ADR 0013 establishes that the platform must support customer/self-service and external consumers without requiring a first-party customer UI.

The repository already contains important integration building blocks:

- `src/services/integration-service/Program.cs` wires connector management, OAuth flows, webhook validation, Service Bus consumers, and background workers
- `src/services/integration-service/Api/IntegrationEndpoints.cs` already exposes connector, job, inbound-event, OAuth callback, and inbound webhook routes
- Service Bus topics/subscriptions are provisioned centrally in `infra/modules/serviceBus.bicep`
- Durable Functions and background workers already exist across the solution for asynchronous work

However, the integration surface is not yet defined as a product:

- machine-to-machine auth is not formalised in the architecture ADR set
- inbound webhook support exists, but outbound webhook/event productisation is not yet governed
- developer-facing consumption assets are missing as a repo standard
- APIM product boundaries for third-party integrations are not provisioned in infra

The headless platform needs an explicit integration model that covers inbound, outbound, auth, documentation, and operational visibility.

---

## Options Considered

### Option A — Keep integrations as service-local implementation details
This preserves current flexibility but leaves external consumers without a stable operating model. Rejected.

### Option B — Put all integrations directly on business services
This reduces one layer, but duplicates connector logic, webhook handling, and delivery concerns across services. Rejected.

### Option C — Treat integration delivery as a first-class platform surface centred on `integration-service`
Business services continue to own domain events and core business APIs, while `integration-service` becomes the canonical edge for partner connectors, inbound webhooks, outbound dispatch, and integration observability. This matches the current repo shape.

---

## Decision

**Adopt Option C. `integration-service` becomes the canonical platform edge for headless partner integration delivery, while APIM exposes a dedicated `public-integrations` product and business services continue emitting domain events.**

The integration model includes four explicit surfaces:

1. **Partner REST APIs** via APIM
2. **Inbound webhooks** into `integration-service`
3. **Outbound events/webhooks** dispatched from domain events through integration-service workflows
4. **Developer-consumption assets** published alongside the contracts

Machine-to-machine consumption is a supported first-class scenario.

---

## Detail

### Canonical service responsibilities

`integration-service` owns:

- connector registration and lifecycle
- external OAuth connection flows
- inbound webhook validation and translation
- outbound dispatch job management
- integration audit/status visibility

Business services own:

- domain events
- domain-specific validation and business semantics
- internal operational APIs

This keeps connector/webhook concerns out of each domain service while preserving service ownership of business capability.

### Integration surfaces

| Surface | Owner | Primary consumers |
|---|---|---|
| REST management APIs | `integration-service` via APIM | tenant admins, partner automation, support tooling |
| Inbound webhooks | `integration-service` | SaaS partners / external systems |
| Outbound webhooks and dispatch jobs | `integration-service` fed by business events | SaaS partners / external systems |
| Event publication on Service Bus | business services | internal async consumers and integration-service |

### Inbound model

Inbound webhooks continue to terminate in `integration-service`.

Principles:

- endpoint authentication is signature-based or connector-specific validation
- payloads are durably recorded for audit/replay
- validated payloads are translated into internal events/messages
- post-validation processing failures do not leak implementation detail to callers

This aligns with the existing webhook validators and inbound event storage model already present in the repo.

### Outbound model

Outbound integrations are driven from domain events and dispatch jobs rather than direct synchronous coupling.

Standard flow:

1. business service emits a domain event
2. event reaches `integration-service`
3. integration-service determines subscribed connector(s)
4. delivery job is created and retried according to connector policy
5. job state is visible through integration APIs

This builds on the existing outbound job model instead of inventing a parallel mechanism.

### Machine-to-machine auth

Machine clients are now explicitly supported. Browser-oriented MSAL assumptions are not sufficient for partner or automation clients.

R1 standard:

- APIM protects partner APIs
- external machine clients authenticate using Entra app registrations and client-credential flow
- partner/client identity is represented separately from end-user roles
- service-to-service platform traffic continues to use existing internal infrastructure patterns

This ADR does **not** require every domain service to implement separate partner auth immediately, but it requires the external integration surface to stop assuming browser-mediated tokens only.

### Developer-consumption assets

Every supported integration product must ship with:

- OpenAPI document(s)
- webhook contract documentation, including signature rules and headers
- event catalog for supported outbound business events
- example payloads
- importable collection/examples for testing

SDK generation is optional per product, but the contracts must be structured so it becomes feasible.

### APIM product model

APIM must expose a distinct **`public-integrations`** product with:

- versioned APIs
- partner-specific subscriptions/keys or policies as required
- throttling distinct from staff-UI traffic
- only externally supported integration operations

The current infra provisions APIM itself, but not the product model yet. That becomes follow-on implementation work.

### Operational visibility

A usable headless platform must let operators and partners answer:

- Was the webhook received?
- Was it validated?
- Was it translated?
- Was the outbound delivery attempted?
- Is the job pending, failed, replayed, or complete?

The existing inbound-events and jobs endpoints are the right backbone for this and should be formalised as supported operational contracts.

---

## Consequences

### Positive
- Gives partners a stable model for both inbound and outbound integration
- Reuses existing integration-service and Service Bus patterns already in the repo
- Separates business-domain ownership from connector/webhook delivery concerns
- Unlocks APIM productisation and developer-facing assets cleanly

### Negative
- Requires auth and contract work beyond the current browser-oriented ADRs
- Introduces governance overhead for webhook/event schemas
- May expose current implementation drift in integration-service that was previously acceptable while the surface was still internal-ish
