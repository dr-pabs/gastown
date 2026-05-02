# ADR 0008 — Analytics Data Strategy

**Status:** Accepted  
**Date:** 2026-04-04  
**Deciders:** Technical Director  
**Supersedes:** —  
**Superseded by:** —

---

## Context

The platform includes an Analytics module. Analytics is confirmed as **basic in R1** and **comprehensive in R2**. The risk of leaving this undefined is that R1 services are built without consideration for how Analytics will consume their data, making R2 a costly retrofit.

This ADR defines:
1. What "basic Analytics" means in R1, and how it is implemented
2. What "comprehensive Analytics" means in R2, and what R1 must do to enable it without rework
3. The data flow from operational services to the analytics layer in both releases

---

## Context: What CRM Analytics Typically Requires

Analytics in a CRM cuts across module boundaries — a sales dashboard needs lead data (SFA), case volume (CS&S), and campaign attribution (Marketing) in a single view. This cross-domain aggregation is structurally different from the single-domain reads that other services perform.

If Analytics reads directly from the operational database (used by SFA, CS&S, etc.), it:
- Risks performance impact on transactional queries ("noisy neighbour" within the database)
- Is tightly coupled to the operational schema — a schema change in SFA breaks Analytics
- Has no historical record — if a lead is updated, the analytics view shows only current state, not the history

A proper analytics architecture separates the analytical read model from the operational write model (CQRS at a data level).

---

## Options Considered

### Option A — R1: Direct reads from operational DB. R2: Full data warehouse.
Simple R1 implementation — Analytics queries the same Azure SQL Hyperscale database as the operational services, via a read replica. R2 migrates to a separate analytical store. Risk: R1 creates load on the operational database; R2 migration requires retrofitting event emission into all R1 services.

### Option B — R1: Operational DB read replica. R2: Event Hubs → Data Lake + Synapse.
R1 uses a read-only replica (Azure SQL Hyperscale has built-in named replicas — zero cost, zero configuration). R2 adds event streaming from all services to Event Hubs, landing in Azure Data Lake, queried via Azure Synapse Analytics. R1 services must emit analytics events from day one (even if nothing consumes them yet), so R2 has historical data from the platform launch date.

### Option C — R1 + R2: Dedicated analytics database from day one.
A separate Azure SQL database is populated by a sync process from day one. Avoids the R2 migration problem but adds operational complexity and cost to R1 where the analytics requirements are simple.

---

## Decision

**Adopt Option B.**

- **R1:** Analytics reads from an Azure SQL Hyperscale named replica (read-only). Analytics queries are contained to a dedicated `analytics-service` — no other service queries the replica.
- **R1 (also):** All operational services emit domain events to the `crm.analytics` Service Bus topic from day one. These events are not consumed in R1 but provide the historical record for R2.
- **R2:** An Azure Event Hubs → Data Lake → Synapse Analytics pipeline is added. The `analytics-service` is updated to query Synapse for historical/aggregate data, and the operational replica for near-real-time current data.

---

## Detail

### R1 Analytics — What It Covers

R1 Analytics is intentionally scoped to **pre-built dashboards on current operational data** with a max look-back of 90 days (the retention period on the read replica).

| Dashboard | Data source | Metrics |
|-----------|------------|---------|
| Sales Pipeline | SFA — leads, opportunities | Pipeline value by stage, conversion rate, avg deal size |
| Lead Acquisition | SFA — leads | Leads by source, by rep, by period |
| Case Volume | CS&S — cases | Open/closed by period, avg resolution time, SLA breach rate |
| Campaign Performance | Marketing — campaigns | Send volume, open rate, click rate, conversion attribution |
| Tenant Usage | Platform — audit log | API call volume, active users (for PlatformAdmin view only) |

No custom query capability in R1. No natural language querying in R1 (deferred to R2 with AI integration per ADR 0007).

### R1 Data Flow

```
Operational services (SFA, CS&S, Marketing)
  → write to Azure SQL Hyperscale (primary)
  → Azure SQL Hyperscale named replica (read-only, zero-lag behind primary)
      → analytics-service queries only this replica
          → pre-built dashboard queries, paginated, tenant-scoped
              → staff portal dashboards (via APIM + BFF)

Simultaneously (R1, for R2 readiness):
Operational services
  → emit domain events to crm.analytics Service Bus topic
      → no subscriber in R1 (events are dead-lettered after TTL — acceptable, TTL set long)
```

### R1 — analytics-service Constraints

- Reads **only** from the named replica — never the primary. Connection string is replica-only, enforced via Key Vault secret naming (`sql-analytics-readonly-connection-string`).
- All queries are tenant-scoped via the same `ITenantContext` + EF Core query filter mechanism as operational services (ADR 0001 applies).
- Queries exceeding 10 seconds are killed by a query timeout. No unbounded aggregations.
- Results are paginated and/or pre-aggregated — no returning raw record sets to the frontend.
- The `analytics-service` has its own EF Core `DbContext` (`AnalyticsDbContext`) with **read-only** entity configurations. It does not share `DbContext` with operational services.

### Analytics Events (R1 — emitted, not yet consumed)

All operational services must emit events to the `crm.analytics` Service Bus topic. These are **in addition to** their existing domain events on `crm.sfa`, `crm.css`, etc.

Event schema (all analytics events):

```json
{
  "eventId": "{guid}",
  "eventType": "sfa.lead.created",
  "tenantId": "{guid}",
  "occurredAt": "2026-04-04T10:00:00Z",
  "subjectId": "{guid}",
  "subjectType": "Lead",
  "payload": { ... }
}
```

`payload` contains **anonymised or aggregated data only** — no PII. Field-level guidance:

| Field | Allowed in payload | Rationale |
|-------|-------------------|-----------|
| Lead score | ✅ | Numeric, no PII |
| Lead source | ✅ | Categorical |
| Industry / region | ✅ | Categorical |
| Contact name | ❌ | PII — omit |
| Contact email | ❌ | PII — omit |
| Company name | ⚠️ | Include only if needed for analytics; treat as sensitive |

The `payload` schema per event type is defined in `src/services/{service}/Analytics/Events/`. Agents must define analytics event schemas alongside feature development — not retrofitted later.

Required analytics events per module (R1 minimum):

| Module | Events |
|--------|--------|
| SFA | `lead.created`, `lead.qualified`, `lead.converted`, `lead.lost`, `opportunity.created`, `opportunity.won`, `opportunity.lost`, `opportunity.stage.changed` |
| CS&S | `case.created`, `case.assigned`, `case.resolved`, `case.escalated`, `sla.breached` |
| Marketing | `campaign.sent`, `campaign.opened`, `campaign.clicked`, `journey.entered`, `journey.completed`, `journey.exited` |
| Platform | `tenant.provisioned`, `user.login` (count only — no identity data) |

### R2 Analytics — Architecture (Design Intent, not binding until R2 ADR)

R2 will add:

```
crm.analytics Service Bus topic
  → Azure Event Hubs (high-throughput ingestion)
      → Azure Data Lake Gen2 (raw event store, partitioned by tenant + date)
          → Azure Synapse Analytics (SQL pools for aggregate queries)
              → analytics-service (updated to query Synapse for historical data)
                  → Natural language query via ADR 0007 ai-gateway
```

Because analytics events are emitted from R1 day one, R2 will have full historical data from the platform launch date — no backfill required.

R2 will also add:
- Custom report builder (drag-and-drop, tenant-configurable)
- Natural language querying ("show me deals closed last quarter by region")
- Scheduled report delivery (email PDF)
- Cross-tenant aggregate reporting for PlatformAdmin

A separate ADR will govern the R2 analytics architecture in detail before R2 development begins.

### Tenant Isolation in Analytics

All analytical queries are subject to the same tenant isolation rules as operational queries (ADR 0001):

- EF Core query filters on `TenantId` apply to `AnalyticsDbContext`
- `SESSION_CONTEXT` is set on the replica connection before each query
- The mandatory tenant isolation test applies to every analytics endpoint
- PlatformAdmin aggregate queries (cross-tenant) are a separate code path, gated by the `PlatformAdmin` role (ADR 0004), and explicitly excluded from standard tenant-scoped paths

---

## Consequences

### Positive
- R1 implementation is simple — read replica, no new infrastructure
- R2 is unblocked from day one — analytics events provide historical data without backfill
- Replica connection is separated from operational connection — analytics load cannot affect transactional performance
- Tenant isolation is consistent with the rest of the platform
- No schema coupling between analytics and operational layers in R2 — Synapse reads from events, not tables

### Negative
- Every R1 service must emit analytics events — this is additional work per feature
- Analytics events must be defined and maintained alongside domain events — discipline required
- R1 look-back is capped at 90 days (replica retention) — acceptable for R1 dashboard use cases
- R1 analytics is read-only and pre-built — no custom queries until R2

---

## Open Questions (to be resolved in R2 ADR)

- Azure Synapse vs Azure Data Explorer for the R2 analytical store — depends on query patterns (SQL familiarity vs time-series)
- Chargeback model for Synapse query costs per tenant
- Data residency requirements for client-hosted analytics data — does the Data Lake live in the client's subscription?
