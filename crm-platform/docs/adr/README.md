# Architecture Decision Records

This directory contains Architecture Decision Records (ADRs) for the CRM Platform.

## What is an ADR?

An ADR documents a significant architectural decision: the context, the options considered, the decision made, and the consequences. They are immutable once accepted — if a decision is reversed, a new ADR supersedes it.

Files are numbered sequentially: `NNNN-short-title.md`

---

## ADR Index

| ADR | Title | Status | Date |
|-----|-------|--------|------|
| [0001](./0001-multi-tenant-database-strategy.md) | Multi-Tenant Database Strategy | Accepted | 2026-03-29 |
| [0002](./0002-service-to-service-communication.md) | Service-to-Service Communication via Service Bus | Accepted | 2026-03-29 |
| [0003](./0003-iac-bicep.md) | IaC: Bicep over Terraform | Accepted | 2026-03-29 |
| [0004](./0004-authentication-and-authorisation.md) | Authentication and Authorisation Architecture | Accepted | 2026-04-04 |
| [0005](./0005-api-gateway-and-entry-point.md) | API Gateway and External Entry Point | Accepted | 2026-04-04 |
| [0006](./0006-frontend-architecture.md) | Frontend Architecture | Accepted | 2026-04-04 |
| [0007](./0007-ai-integration-pattern.md) | AI Integration Pattern | Accepted | 2026-04-04 |
| [0008](./0008-analytics-data-strategy.md) | Analytics Data Strategy | Accepted | 2026-04-04 |
| [0009](./0009-tenant-provisioning-and-lifecycle.md) | Tenant Provisioning and Lifecycle | Accepted | 2026-04-04 |
| [0010](./0010-resilience-and-saga-patterns.md) | Resilience and Saga Patterns | Accepted | 2026-04-04 |
| [0011](./0011-cicd-pipeline-architecture.md) | CI/CD Pipeline Architecture | Accepted | 2026-04-04 |
| [0012](./0012-local-development-experience.md) | Local Development Experience | Accepted | 2026-04-04 |
| [0013](./0013-headless-platform-and-retained-staff-portal.md) | Headless Platform and Retained Staff Portal | Proposed | 2026-04-30 |
| [0014](./0014-headless-api-contract-standards.md) | Headless API Contract Standards | Proposed | 2026-04-30 |
| [0015](./0015-headless-integration-surfaces.md) | Headless Integration Surfaces | Proposed | 2026-04-30 |
| [0016](./0016-retained-staff-portal-and-ui-retirement-topology.md) | Retained Staff Portal and UI Retirement Topology | Proposed | 2026-04-30 |

---

## ADR Summaries

### Foundation

**[ADR 0001 — Multi-Tenant Database Strategy](./0001-multi-tenant-database-strategy.md)**
Shared Azure SQL Hyperscale database with `TenantId` on every table, SQL Row-Level Security enforced at the database level, and EF Core global query filters as application-level defence in depth. `SESSION_CONTEXT` is set before each query. Tenant isolation is verified by a mandatory test on every API endpoint — a CI gate with no exceptions.

**[ADR 0002 — Service-to-Service Communication via Service Bus](./0002-service-to-service-communication.md)**
All cross-service communication uses Azure Service Bus topics/subscriptions. Direct HTTP calls between backend services are prohibited. Exceptions: APIM → backend services (client boundary), BFF → backend services (UI aggregation), and service → ai-gateway (synchronous AI calls). All exceptions are documented in the relevant ADRs.

**[ADR 0003 — IaC: Bicep over Terraform](./0003-iac-bicep.md)**
Azure Bicep for all infrastructure as code. Azure-native, no state file, first-class support for all Azure resource types. PSRule.Rules.Azure provides Bicep-aware linting and Well-Architected Framework checks.

---

### Identity and Access

**[ADR 0004 — Authentication and Authorisation Architecture](./0004-authentication-and-authorisation.md)**
Two identity providers: Azure Entra ID for staff users, Azure Entra External ID (CIAM) for customer users. Two-layer JWT validation: APIM validates signature and forwards claims; per-service middleware resolves `TenantId` from a tenant registry and loads user roles. Roles are stored in the database (not in the JWT) for runtime configurability. `PlatformAdmin` is a platform-level role separate from all tenant roles.

**[ADR 0005 — API Gateway and External Entry Point](./0005-api-gateway-and-entry-point.md)**
Azure API Management is the sole external entry point. All Container Apps services use internal ingress only — no direct internet exposure. APIM owns JWT validation (Layer 1), tenant throttling, API versioning, CORS, and claim forwarding. A Backend for Frontend (BFF) service aggregates calls for the staff portal. The customer portal calls APIM-proxied endpoints directly. Azure Front Door (global CDN + WAF) is deferred to R2.

---

### Frontend

**[ADR 0006 — Frontend Architecture](./0006-frontend-architecture.md)**
pnpm monorepo with Turborepo, shared packages (`ui`, `auth`, `api-client`, `types`, `utils`). MSAL React for authentication against Entra ID (staff) and Entra External ID (customers). TanStack Query for server state; Zustand for client-only UI state. Generated API client from OpenAPI specs — no hand-written HTTP calls. shadcn/ui (Radix + Tailwind) as the component foundation. TypeScript `strict: true`, ESLint, Prettier, Vitest (80% coverage gate), Playwright E2E on staging.

---

### AI

**[ADR 0007 — AI Integration Pattern](./0007-ai-integration-pattern.md)**
Dedicated `ai-gateway` service is the only service permitted to call Azure AI Foundry. All other services call the gateway via internal HTTP (synchronous) or Service Bus (async). Prompts are stored in the database (not in code), versioned, and rendered by the gateway. PII scrubbing applied to all variables before prompt rendering. Per-tenant token accounting for usage-based billing. Circuit breaker and graceful degradation mandatory — AI features must never break non-AI workflows. Multi-step AI workflows use Durable Functions orchestration.

---

### Data and Analytics

**[ADR 0008 — Analytics Data Strategy](./0008-analytics-data-strategy.md)**
R1: Pre-built dashboards on an Azure SQL Hyperscale named replica (read-only, zero-lag). `analytics-service` queries the replica only — never the primary. All operational services emit analytics events to the `crm.analytics` Service Bus topic from day one (not consumed in R1, providing historical data for R2 without backfill). R2: Event Hubs → Data Lake → Synapse Analytics pipeline, natural language querying via ADR 0007 ai-gateway. Analytics payload contains no PII.

---

### Platform Operations

**[ADR 0009 — Tenant Provisioning and Lifecycle](./0009-tenant-provisioning-and-lifecycle.md)**
Tenant lifecycle states: `Provisioning → Active → Suspended → Deprovisioning → Deprovisioned → Erased`. Provisioning is a Durable Function orchestration (idempotent, compensating, auditable) — the shell script is retired for production. Deprovisioning includes a data export step and awaits confirmation before deletion. GDPR erasure completed within 72 hours. Audit log retained 7 years in cold storage. Client-hosted provisioning uses Bicep deployment followed by the same Durable Function steps.

**[ADR 0010 — Resilience and Saga Patterns](./0010-resilience-and-saga-patterns.md)**
Polly (Microsoft.Extensions.Resilience) pipeline on all outbound calls: timeout → retry (exponential backoff with jitter) → circuit breaker → bulkhead. Policies defined once in `_template`, inherited by all services. Service Bus consumers: idempotency check (base class, cannot be bypassed), `MaxDeliveryCount: 10`, DLQ monitoring with 15-minute alert threshold. Saga pattern: choreography for simple independent reactions; Durable Functions orchestration with compensation for multi-step cross-service operations. Four R1 sagas identified: campaign launch, lead conversion, tenant suspension, case escalation.

---

### Engineering

**[ADR 0011 — CI/CD Pipeline Architecture](./0011-cicd-pipeline-architecture.md)**
GitHub Actions with path-filtered per-service workflows (backend) and Turborepo affected detection (frontend). Authentication to Azure via Workload Identity Federation — no Azure credentials in GitHub Secrets. Four environments: `dev` (automatic), `test` (automatic, integration tests), `staging` (manual gate, Playwright E2E, 24h soak), `prod` (manual gate, automatic rollback on health failure). Rolling deployment default; blue/green opt-in for high-risk releases. Client-hosted updates are pull-based — the platform never pushes into client subscriptions.

**[ADR 0012 — Local Development Experience](./0012-local-development-experience.md)**
Docker Compose provides local SQL Server (with RLS policy applied), Azure Service Bus emulator (topics/subscriptions pre-created), Azurite, and Mailpit. `make dev` starts the full stack; `make dev-{service}` starts a single service with its dependencies. No security bypasses in local development — `SESSION_CONTEXT`, tenant isolation, and auth middleware behave identically to production. Auth stub (`src/services/_local/auth-stub/`) issues signed local JWTs for offline development; blocked from running in any non-Development environment. Dev Azure subscription provides Key Vault, AI Foundry, and real Entra tenants for realistic testing.

**[ADR 0013 — Headless Platform and Retained Staff Portal](./0013-headless-platform-and-retained-staff-portal.md)**
The platform is reset to a hybrid headless model: the staff portal remains the sole first-party UI, the customer portal is retired, APIM remains the single external entry point, and customer/self-service plus third-party consumption move to headless APIs, events, and integration products. A dedicated `staff-bff` is no longer mandatory.

**[ADR 0014 — Headless API Contract Standards](./0014-headless-api-contract-standards.md)**
External contracts remain service-owned, but they must follow shared standards for OpenAPI publication, endpoint visibility, error handling, pagination, idempotency, correlation, and long-running operations so APIM can publish stable headless products.

**[ADR 0015 — Headless Integration Surfaces](./0015-headless-integration-surfaces.md)**
`integration-service` becomes the canonical partner-integration edge for REST management APIs, inbound webhooks, outbound dispatch, and integration observability. Machine-to-machine consumers are now first-class and APIM must expose a dedicated integration product.

**[ADR 0016 — Retained Staff Portal and UI Retirement Topology](./0016-retained-staff-portal-and-ui-retirement-topology.md)**
The staff portal is the only active first-party UI in CI, infra, docs, and operations. The customer portal is retired from active topology and may remain only as temporary reference code until headless replacement contracts are complete.

---

## Proposing a New ADR

1. Copy `docs/adr/_template.md` to `docs/adr/NNNN-short-title.md` (next sequential number)
2. Fill in all sections — Context, Options Considered, Decision, Detail, Consequences
3. Set Status to `Proposed`
4. Open a pull request — the ADR is discussed and revised in the PR
5. On merge, status changes to `Accepted`
6. ADRs are **immutable once accepted** — if a decision changes, create a new ADR with `Supersedes: NNNN`

## ADR Status Values

| Status | Meaning |
|--------|---------|
| `Proposed` | Under discussion in a PR |
| `Accepted` | Approved and in effect |
| `Superseded` | Replaced by a later ADR (link to successor) |
| `Deprecated` | No longer applicable — not superseded by a specific decision |

---

## Reading Order for New Agents

If you are an agent being onboarded to this codebase, read ADRs in this order before writing any code:

1. Root `CLAUDE.md` — mandatory rules that apply everywhere
2. ADR 0001 — how data isolation works (affects every database interaction)
3. ADR 0004 — how authentication and authorisation works (affects every endpoint)
4. ADR 0002 — how services communicate (affects all cross-service interactions)
5. ADR 0005 — where your service sits in the request flow
6. ADR 0012 — how to set up and run the platform locally
7. ADR 0013 — target product shape for retained UI vs headless capability
8. ADR 0014 — external API contract rules for headless delivery
9. ADR 0015 — partner/webhook/event integration delivery model
10. ADR 0016 — retained UI and retired UI topology
11. The service-level `CLAUDE.md` for the specific service you are working on
12. Remaining ADRs as relevant to your feature area
