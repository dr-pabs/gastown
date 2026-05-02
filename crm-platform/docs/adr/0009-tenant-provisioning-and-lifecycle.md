# ADR 0009 — Tenant Provisioning and Lifecycle

**Status:** Accepted  
**Date:** 2026-04-04  
**Deciders:** Technical Director  
**Supersedes:** —  
**Superseded by:** —

---

## Context

Tenant provisioning in a multi-tenant SaaS platform is a critical business operation. A new tenant must be correctly initialised across multiple systems before they can use the platform. A failed or partially completed provisioning leaves the platform in an inconsistent state that is difficult to recover from and creates a poor first experience.

The platform currently has a `scripts/provision-tenant.sh` shell script. This is insufficient for production use because:

- Shell scripts are not idempotent by default — running twice after a failure leaves partial state
- There is no audit trail of what succeeded and what failed
- There is no rollback/compensation if a step fails midway
- A script cannot wait for human approval steps (e.g. compliance sign-off for enterprise clients)
- Deprovision (GDPR erasure, contract end) has no equivalent

This ADR defines:

1. The canonical tenant lifecycle states
2. The provisioning workflow — steps, order, and failure handling
3. The deprovisioning workflow — data retention, export, and erasure
4. How SaaS and client-hosted provisioning differ
5. The implementation mechanism

---

## Tenant Lifecycle States

```
         ┌───────────────────────────────────────────────┐
         ▼                                               │
  [ Provisioning ] ──fail──► [ ProvisioningFailed ]     │
         │                           │                  │
       success                    retry                 │
         │                           │                  │
         ▼                           ▼                  │
     [ Active ] ◄───────── [ Provisioning ]             │
         │                                              │
    ┌────┴────────────────┐                             │
    │                     │                             │
 suspend            contract end                        │
    │                     │                             │
    ▼                     ▼                             │
[ Suspended ]    [ Deprovisioning ]                     │
    │                     │                             │
 reactivate           complete                          │
    │                     │                             │
    └────────►            ▼                             │
           [ Active ]  [ Deprovisioned ]                │
                          │                             │
                       erasure                          │
                       request                          │
                          │                             │
                          ▼                             │
                      [ Erased ] ───────────────────────┘
```

State is stored in `dbo.Tenants.Status`. State transitions are only permitted via the `platform-service` API — no direct database updates to this field are permitted.

---

## Decision

**Implement tenant provisioning and deprovisioning as Azure Durable Function orchestrations, triggered by the `platform-service` API. The shell script is retired for production use and retained only as a developer convenience for local/dev environments.**

---

## Detail

### Provisioning Workflow (SaaS)

The provisioning orchestration is triggered when the `platform-service` receives a `POST /api/v1/platform/tenants` request (PlatformAdmin only). It runs as a Durable Function orchestration in `src/functions/tenant-lifecycle/`.

**Steps (in order, each implemented as a Durable Function activity):**

| Step | Activity | Idempotency key | Rollback on failure |
|------|----------|----------------|-------------------|
| 1 | Create tenant record in `dbo.Tenants` with status `Provisioning` | `TenantId` (idempotent insert) | N/A — first step |
| 2 | Publish `tenant.provisioning.started` to `crm.platform` topic | `EventId` (idempotent publish) | Delete tenant record |
| 3 | Register Entra External ID application for customer portal | Idempotent via Entra Graph API | Remove app registration |
| 4 | Create tenant user group in platform Entra ID | Idempotent via Graph API | Remove group |
| 5 | Set up SQL RLS policy for new `TenantId` | Idempotent SQL script | Drop RLS policy |
| 6 | Seed default configuration (`dbo.TenantConfig`) | Idempotent upsert | Delete config rows |
| 7 | Seed default roles (`dbo.UserRoles` for TenantAdmin) | Idempotent upsert | Delete role rows |
| 8 | Create tenant admin invitation (send email via CS&S notification service) | Idempotent — check invite record before sending | Mark invite as cancelled |
| 9 | Set tenant status to `Active` in `dbo.Tenants` | Idempotent update | Set to `ProvisioningFailed` |
| 10 | Publish `tenant.provisioned` to `crm.platform` topic | `EventId` | N/A — final step |

**Failure handling:**

- If any step fails, the orchestration runs the rollback actions for all completed steps in reverse order (compensation pattern)
- The tenant is set to `ProvisioningFailed` status
- A `tenant.provisioning.failed` event is published with the failed step name and error detail
- The PlatformAdmin receives an alert via the `crm.platform` topic subscriber (notification service)
- The orchestration is **retryable** — a PlatformAdmin can trigger a retry, which re-runs only from the failed step (Durable Functions checkpoint/replay handles this)

**Idempotency:**

Every activity checks whether its work is already done before executing. This means re-running the orchestration after a failure is safe — completed steps are skipped, failed steps are retried. The `TenantId` is the idempotency key for the entire orchestration.

### Provisioning Workflow (Client-Hosted)

Client-hosted provisioning differs in infrastructure scope:

| Step | SaaS | Client-Hosted |
|------|------|---------------|
| Azure subscription | Platform subscription | Client's subscription |
| Entra ID | Platform Entra | Client's Entra (pre-existing) |
| Entra External ID | Platform External ID | New External ID tenant in client subscription |
| Azure SQL | Shared Hyperscale | Dedicated Azure SQL in client subscription |
| APIM | Shared platform APIM | Dedicated APIM in client subscription |
| Container Apps | Shared platform ACA environment | Dedicated ACA environment |

Client-hosted provisioning is a **Bicep deployment** (`infra/client-hosted/main.bicep`) triggered by the `scripts/provision-client-hosted.sh` script (which wraps `az deployment sub create`). This is acceptable for client-hosted because:
- It is a rare, human-supervised operation (not automated SaaS self-service)
- Azure Resource Manager provides idempotency for Bicep deployments
- The Bicep deployment is followed by the same Durable Function steps 1, 6, 7, 8, 9, 10 from the SaaS workflow above (steps 2–5 are replaced by the Bicep deployment itself)

A dedicated runbook (`docs/runbooks/provision-client-hosted.md`) governs the full client-hosted provisioning procedure, including pre-flight checks.

### Tenant Suspension

Suspension is triggered when:
- A payment fails (billing service event on `crm.platform`)
- A PlatformAdmin manually suspends a tenant

On suspension:
- `dbo.Tenants.Status` is set to `Suspended`
- All API requests for the tenant return HTTP 402 (Payment Required) with a structured `ProblemDetails` body
- The check occurs in the `ITenantContext` resolution middleware — before any controller logic
- Data is retained unchanged — suspension is reversible

Reactivation reverses the status to `Active`. No data changes required.

### Deprovisioning Workflow

Triggered by contract end or customer request. This is a **planned, human-approved** operation — not automated.

**Data retention policy (R1 defaults, configurable per client agreement):**

| Data class | Retention after deprovisioning |
|-----------|-------------------------------|
| Transactional data (leads, cases, campaigns) | 90 days in `Deprovisioning` state, then hard delete |
| Audit log | 7 years (legal requirement — retained in cold storage) |
| AI usage records | 90 days, then aggregate-only retention |
| User identity data | Deleted within 30 days (GDPR Article 17) |
| Backups | Retained per Azure SQL backup policy (35 days), then expired |

**Deprovisioning steps:**

| Step | Detail |
|------|--------|
| 1 | Set status to `Deprovisioning`. All API access blocked (HTTP 410 Gone). |
| 2 | Publish `tenant.deprovisioning.started` to `crm.platform` |
| 3 | Generate and deliver data export to tenant admin (all tenant data as JSON/CSV, encrypted, delivered via secure link) |
| 4 | Await confirmation of export receipt (Durable Function external event — human trigger) |
| 5 | Delete transactional data (soft-deleted records are hard-deleted; `IsDeleted` rows purged) |
| 6 | Delete Entra user group and External ID app registration |
| 7 | Delete SQL RLS policy for `TenantId` |
| 8 | Delete tenant config and role records |
| 9 | Set status to `Deprovisioned`. Tenant record retained for audit trail. |
| 10 | Publish `tenant.deprovisioned` to `crm.platform` |

For GDPR erasure requests, step 5 is performed immediately after step 4, with a 72-hour SLA from request receipt.

The audit log is **never** deleted as part of deprovisioning — it is moved to Azure Blob cold storage with a 7-year retention lock.

### Tenant Configuration Defaults

On provisioning, the following defaults are seeded into `dbo.TenantConfig`:

| Key | Default value | Notes |
|-----|--------------|-------|
| `modules.sfa.enabled` | `true` | |
| `modules.css.enabled` | `true` | |
| `modules.marketing.enabled` | `false` | Enabled per plan tier |
| `modules.analytics.enabled` | `true` | Basic analytics always on |
| `ai.monthly_token_limit` | `500000` | Overridden by plan |
| `cases.sla.default_hours` | `24` | Tenant-configurable |
| `leads.score.decay_enabled` | `true` | |

All config values are stored as key-value strings. The `platform-service` provides typed access via `ITenantConfig<T>`. Config changes publish a `tenant.config.updated` event on `crm.platform` so dependent services can invalidate their caches.

---

## Consequences

### Positive
- Durable Function orchestration is inherently idempotent and retryable — no more half-provisioned tenants
- Every provisioning step is auditable via the Durable Functions history log
- Compensation pattern means failures roll back cleanly
- Data retention and GDPR erasure are explicit, tested workflows — not ad-hoc scripts
- Tenant config defaults make new tenants immediately usable without manual setup

### Negative
- Durable Function orchestration is more complex to implement than a shell script
- Client-hosted provisioning still requires human supervision — it cannot be fully self-service in R1
- The data export step in deprovisioning (step 3) requires generating potentially large data sets — must be implemented as a background job, not a synchronous operation
- 7-year audit log retention in cold storage adds ongoing (small) storage cost per deprovisioned tenant

---

## Shell Script Retirement

`scripts/provision-tenant.sh` is retained for **local and dev environment use only**. It must not be used in test, staging, or production. A warning comment is added to the script header. The CI pipeline for the `test` environment and above uses only the Durable Function orchestration.
