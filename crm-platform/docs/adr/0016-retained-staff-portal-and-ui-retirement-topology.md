# ADR 0016 — Retained Staff Portal and UI Retirement Topology

**Status:** Proposed  
**Date:** 2026-04-30  
**Deciders:** Technical Director  
**Supersedes:** —  
**Superseded by:** —

---

## Context

ADR 0013 establishes that `staff-portal` is the only retained first-party UI and that `customer-portal` is no longer an active shipped surface.

The repository has already started moving in that direction:

- `.github/workflows/ci-frontend.yml` now builds only `staff-portal`
- `infra/platform/main.bicep` provisions only one Static Web App
- `README.md` and local-development docs have been updated toward a single retained UI

But the topology is not yet fully aligned:

- `src/frontend/customer-portal` still exists in the tree as a retired codebase
- older ADRs still describe a two-portal strategy
- some docs and module comments still refer to two Static Web Apps or customer-portal delivery concerns
- the previous frontend ADR assumes shared frontend evolution across both portals, which is no longer the target

The platform needs an explicit topology decision for what is active, what is retired, and what is merely kept temporarily in the repository.

---

## Options Considered

### Option A — Keep customer-portal as a dormant but still buildable deployment target
This preserves optionality but keeps the platform mentally and operationally tied to a dual-portal model. Rejected.

### Option B — Delete customer-portal immediately from the repository
This is clean, but it removes a useful reference while the replacement headless contracts are still being shaped. Deferred.

### Option C — Retire customer-portal operationally now, keep it only as temporary reference code until contract replacement is complete
This removes the portal from active topology while keeping the source available for migration/reference during the transition. This fits the current repo state.

---

## Decision

**Adopt Option C. `staff-portal` is the only active first-party UI in CI, infra, docs, and operations. `customer-portal` is a retired reference surface only and must not be treated as an active deployment or support target.**

This means:

- only one Static Web App is provisioned
- only one frontend app is built and tested in active CI
- customer-portal auth, provisioning, and runtime assumptions are removed from active documentation
- future frontend investment applies only to `staff-portal`

---

## Detail

### Active topology

The active topology becomes:

```text
staff users
  -> staff-portal
  -> APIM
  -> service APIs / composition where needed

customer and partner consumers
  -> APIM products
  -> headless APIs / webhooks / events
```

There is no active first-party customer-facing web application in the target topology.

### Repository treatment of `customer-portal`

`src/frontend/customer-portal` is now classified as:

- **retired**
- **not built in active CI**
- **not provisioned in infra**
- **not referenced as a supported runtime surface**

It may remain temporarily in the repository only to:

- compare legacy behavior during migration
- map customer journeys to replacement headless APIs
- assist removal sequencing once the replacement contracts are stable

### Frontend architecture implications

The old dual-portal frontend strategy no longer governs new work.

Implications:

- no new shared-package investment should be justified by customer-portal reuse
- `staff-portal` should evolve against stable backend contracts, not against assumptions of a future two-portal monorepo
- customer-facing capabilities now belong in backend/API and integration workstreams, not in a second SPA roadmap

### Infra and CI implications

The platform now assumes:

- one Static Web App in `infra/platform/main.bicep`
- one active frontend CI workflow target
- no customer portal deployment token/output dependencies
- no customer portal app registration guidance in the active runbooks

Where residual references remain, they are drift to be cleaned up rather than signals of supported topology.

### Cleanup sequence

The UI-retirement topology is complete when all of the following are true:

1. no active CI job builds/tests/deploys `customer-portal`
2. no infra template provisions customer-portal hosting
3. no operational/runbook document describes customer-portal as a supported surface
4. ADRs and top-level docs no longer describe a dual-portal target
5. the retired source can be deleted once its headless replacement contracts are verified

---

## Consequences

### Positive
- Removes operational ambiguity about which UI is actually supported
- Simplifies hosting, CI, provisioning, and local-development assumptions
- Keeps migration focus on contract replacement instead of portal revival
- Preserves the retired source just long enough to support the headless transition

### Negative
- Requires continued discipline to stop stale documentation from reintroducing the old model
- Leaves a temporarily retained codebase in the repository that is intentionally non-active
- Some older ADR text remains useful historically but misleading unless clearly read with the newer decisions
