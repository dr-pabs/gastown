# ADR 0013 — Headless Platform and Retained Staff Portal

**Status:** Proposed  
**Date:** 2026-04-30  
**Deciders:** Technical Director  
**Supersedes:** 0005, 0006  
**Superseded by:** —

---

## Context

The current repository and the accepted ADR set are no longer aligned with the intended product direction.

The original architecture assumed:

- two first-party UIs (`staff-portal` and `customer-portal`)
- a mandatory `staff-bff` layer for the staff portal
- frontend-focused delivery for customer self-service journeys

The target product has changed:

- the **staff portal remains** as the only first-party UI
- the **customer portal is retired**
- customer/self-service capability must be delivered through **headless APIs, events, and integrations**
- third-party/system consumers become a first-class concern rather than a side channel

During implementation review, three issues became clear:

1. The repository already behaves more like an API-first backend than a dual-portal product.
2. The `staff-bff` described in ADR 0005 is not present in the checked-in solution.
3. The frontend architecture in ADR 0006 does not match the checked-in repository shape.

The architecture needs to be reset around the actual target:

1. What remains of the first-party UI estate?
2. What is the canonical external contract for customer/self-service use cases?
3. Is a dedicated BFF mandatory for the retained staff portal?
4. How should APIM expose different products for staff, customer/self-service, and integration consumers?

---

## Options Considered

### Option A — Keep both portals and add headless APIs beside them
This minimises short-term disruption, but it preserves duplicated product surfaces and keeps customer experience coupled to a first-party UI that is no longer strategic.

### Option B — Remove all first-party UIs and go fully headless
This is clean architecturally, but it discards the staff portal that remains important for internal CRM workflows and operational efficiency.

### Option C — Keep the staff portal, retire the customer portal, and make customer/integration capability headless
This retains the high-value internal UI while moving all external/customer-facing capability behind stable APIs and integration surfaces. It matches the requested target and the current codebase direction.

---

## Decision

**Adopt Option C. The staff portal remains the sole first-party UI. The customer portal is retired. Customer/self-service and external consumption move to headless APIs, events, and integrations exposed through APIM-governed products.**

Additionally:

- APIM remains the single external entry point
- a dedicated `staff-bff` is **not mandatory** in R1 of the redesign
- composition may be implemented within bounded-context APIs or by a future composition service only where needed

---

## Detail

### Target product shape

The target platform consists of:

- **Backend domain services** for CRM capability
- **Durable Functions and async workers** for orchestration and time-based workflows
- **One first-party UI**: `src/frontend/staff-portal`
- **Headless external surfaces** for:
  - customer/self-service case flows
  - third-party integrations
  - machine-to-machine automation

### External channel model

| Consumer | Delivery model | Notes |
|---|---|---|
| Staff users | Staff portal | Internal/productive CRM UI remains first-party |
| Customer/self-service | Headless APIs via APIM | No first-party customer UI is required |
| Third-party integrations | Headless APIs via APIM + webhooks/events | Contract-first, versioned, documented |
| Internal automation | Service Bus / orchestration / internal APIs | Not internet-facing |

### Entry-point architecture

APIM remains the sole external entry point. All external traffic continues through APIM for:

- authentication and issuer validation
- throttling
- API versioning
- routing
- correlation and request policy enforcement

The main change is **product segmentation**, not entry-point replacement.

### APIM product strategy

APIM should expose distinct products:

| Product | Primary consumers | Characteristics |
|---|---|---|
| `staff-ui` | staff portal | Internal CRM operations, staff-scoped auth, higher request volume tolerance |
| `customer-self-service` | customer/mobile/web clients owned by tenants or partners | Narrow CS&S-focused contract, customer-safe operations only |
| `public-integrations` | third-party and machine clients | Explicit contract, stronger idempotency and lifecycle guarantees |

### Staff portal architecture

The staff portal remains supported, but the redesign **does not require** a dedicated `staff-bff` as a prerequisite.

Instead:

- the staff portal should prefer direct calls to stable service APIs through APIM
- where staff workflows need aggregation, composition can be implemented by:
  - bounded-context read models, or
  - a future composition API/BFF only for proven high-chattiness scenarios

This replaces the prior assumption that a mandatory `staff-bff` exists.

### Customer/self-service architecture

Customer/self-service capabilities are delivered headlessly. In R1 of the redesign, the canonical example is **case management via `css-service`**.

Expected customer/self-service contract characteristics:

- customer-safe case list/detail operations
- case creation
- public comment/reply flows
- case close / resolution acknowledgement flows where appropriate
- explicit filtering of staff-only/internal data
- contract-level pagination and stable DTOs

The customer-facing API is a **product surface**, not a UI surface.

### Integration architecture

Headless delivery requires more than CRUD APIs. The target architecture must support:

- stable REST APIs
- outbound webhooks for business events
- event publication for async consumers
- machine-to-machine auth scenarios
- developer-facing contract assets (OpenAPI, examples, collections, SDK generation where warranted)

### Repository topology implications

The active repository topology becomes:

```
src/frontend/
  staff-portal/        → retained first-party CRM UI

src/services/
  ...                  → domain services and headless API surfaces
```

`src/frontend/customer-portal` is treated as a retired surface and should be removed from active CI, infra, and operational assumptions.

### ADR impact

This ADR supersedes the following earlier decisions:

1. **ADR 0005**
   - supersedes the assumption that the customer portal remains a first-class frontend
   - supersedes the assumption that a dedicated `staff-bff` is mandatory
   - retains APIM as the single external entry point

2. **ADR 0006**
   - supersedes the assumption of a dual-portal frontend strategy
   - narrows frontend strategy to the retained staff portal only
   - shifts customer capability delivery from frontend architecture to contract architecture

### Immediate implementation consequences

Near-term implementation should follow this sequence:

1. Stabilise shared backend build/template primitives where needed
2. Promote customer/self-service case flows in `css-service` into the supported headless contract
3. Remove customer-portal delivery paths from active CI/infra/docs
4. Define APIM product boundaries and consumer contract publication
5. Continue staff-portal evolution against stable service contracts

---

## Consequences

### Positive
- Aligns architecture with the actual target product direction
- Keeps the high-value staff portal without forcing all product capability through UI delivery
- Makes customer/self-service functionality consumable by multiple client types
- Reduces duplication by removing the second first-party portal from active delivery
- Removes the architecture dependency on a `staff-bff` that is not currently implemented

### Negative
- Requires contract hardening in backend services that were previously tolerated as portal-coupled
- Increases pressure on API quality, versioning, and documentation
- Requires follow-on ADR and implementation work for APIM products, webhooks, and integration delivery
- Leaves some earlier accepted ADR text outdated until the rest of the repo is fully aligned
