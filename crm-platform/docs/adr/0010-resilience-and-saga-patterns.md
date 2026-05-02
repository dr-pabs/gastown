# ADR 0010 — Resilience and Saga Patterns

**Status:** Accepted  
**Date:** 2026-04-04  
**Deciders:** Technical Director  
**Supersedes:** —  
**Superseded by:** —

---

## Context

The platform is a distributed system of 10+ microservices communicating via Azure Service Bus (ADR 0002), with selective synchronous HTTP calls permitted at defined boundaries (APIM → services, BFF → services, callers → ai-gateway per ADRs 0005 and 0007). In any distributed system, partial failures are not exceptional — they are routine. Without explicit resilience patterns:

- A transient Service Bus outage causes cascading message loss
- A slow downstream dependency (AI gateway, external enrichment API) blocks thread pools
- A multi-step business operation (e.g. campaign launch touching Marketing + SFA + CS&S) leaves data in an inconsistent state if one step fails
- Dead-letter queues fill silently and are never remediated
- Developers make inconsistent retry decisions service by service

This ADR defines:

1. Retry and timeout policy for all outbound calls
2. Circuit breaker policy for synchronous HTTP calls
3. Service Bus consumer resilience (retry, DLQ, poison message handling)
4. Saga pattern for multi-step cross-service business operations
5. Bulkhead policy to prevent one tenant's load from affecting others

---

## Options Considered

### Retry Libraries
- **Polly (Microsoft.Extensions.Resilience)** — the de facto standard for .NET resilience. Integrates with `IHttpClientFactory`. Pipelines compose retry, circuit breaker, timeout, and bulkhead as a single policy. First-class support in .NET 8.
- **Custom retry logic** — inconsistent, untested, repeated across services. Rejected.

### Saga Pattern Implementation
- **Durable Functions orchestration** — already present in the platform for journey engine and tenant provisioning (ADR 0009). Provides checkpointed, resumable, auditable orchestration with built-in compensation support. Natural fit.
- **Choreography-only (event chains)** — services react to each other's events with no central coordinator. Simpler per-service, but distributed state is invisible and compensation is very hard to implement correctly. Rejected for complex multi-step operations.
- **Hybrid** — choreography for simple, independent event reactions (e.g. a lead created event triggering a welcome email); orchestration for multi-step operations with compensation requirements. This is the correct approach.

---

## Decision

**Use Polly (Microsoft.Extensions.Resilience) for all outbound resilience policies. Use Durable Functions orchestration for sagas requiring compensation. Use choreography for simple, independent event reactions. All policies are defined in shared configuration in the `_template` service and must not be overridden without justification.**

---

## Detail

### Shared Resilience Pipeline

Every service inherits the base resilience configuration from the `_template` service (`src/services/_template/Resilience/ResiliencePolicies.cs`). The pipeline is applied via `IHttpClientFactory` for HTTP calls and a custom `IServiceBusPublisher` wrapper for outbound Service Bus messages.

The standard pipeline (applied in this order):

```
Request
  → [1] Timeout policy
  → [2] Retry policy
  → [3] Circuit breaker
  → [4] Bulkhead (concurrency limiter)
  → Outbound call
```

#### 1. Timeout Policy

| Call type | Timeout |
|-----------|---------|
| Internal HTTP (BFF → service, caller → ai-gateway) | 10 seconds |
| AI gateway calls (ai-gateway → Azure AI Foundry) | 30 seconds |
| Service Bus publish | 5 seconds |
| Database query (EF Core command timeout) | 15 seconds |

Timeouts are **per attempt**, not total. The retry policy (below) may result in a total elapsed time of up to `timeout × (1 + retries)` before giving up.

Database command timeout is set on `DbContext` options in the `_template`, not per-query. Overriding to a higher value requires a comment referencing a GitHub issue explaining why.

#### 2. Retry Policy

**HTTP calls:**

```
Max attempts:     3 (1 initial + 2 retries)
Delay strategy:   Exponential backoff with jitter
Base delay:       500ms
Max delay:        10s
Retry on:         HTTP 429, 502, 503, 504 and transient network exceptions
Do NOT retry on:  HTTP 400, 401, 403, 404, 409, 422 (client errors — retrying won't help)
```

**Service Bus publish:**

```
Max attempts:     5
Delay strategy:   Exponential backoff with jitter
Base delay:       200ms
Max delay:        5s
Retry on:         ServiceBusException (transient), timeout
```

Jitter is applied to prevent thundering herd when multiple service instances retry simultaneously after a downstream recovery.

#### 3. Circuit Breaker

Applied to synchronous HTTP calls only (Service Bus has its own reliability guarantees).

```
Failure threshold:      50% failure rate
Sampling duration:      30 seconds
Minimum throughput:     10 requests in sampling window (avoids tripping on low volume)
Break duration:         60 seconds (circuit open — calls fail immediately with no attempt)
Half-open probe count:  3 (allows 3 requests through; if they succeed, circuit closes)
```

When the circuit is open, the calling service returns the appropriate degraded response immediately rather than waiting for a timeout:
- BFF → service circuit open: return cached response if available, otherwise HTTP 503 with `Retry-After: 60`
- Service → ai-gateway circuit open: return graceful degradation response (AI feature unavailable)

Circuit breaker state is **per service instance**, not shared. This is acceptable — each Container Apps replica independently decides to open its circuit based on its own observed failure rate.

#### 4. Bulkhead (Concurrency Limiter)

Prevents one tenant's high-volume requests from exhausting the thread pool and affecting other tenants.

```
Max concurrent calls per service instance:  50
Queue depth (waiting calls):                25
Queue timeout:                              2 seconds
Overflow response:                          HTTP 503 with Retry-After header
```

For the `ai-gateway` specifically, an additional **per-tenant concurrency limit** is applied:

```
Max concurrent AI calls per tenant:  5
Overflow:                            HTTP 429 with structured error (plan limit context)
```

This is enforced in the `ai-gateway` using an in-memory `SemaphoreSlim` keyed by `TenantId`. It is not distributed — it limits per instance. A distributed limit (Redis-backed) is deferred to R2 if per-tenant fairness becomes a concern at scale.

---

### Service Bus Consumer Resilience

Every Service Bus consumer (subscribing to topics on `crm.sfa`, `crm.css`, etc.) follows this pattern, defined in the `_template` base consumer class.

#### Message Processing Pipeline

```
Message received from Service Bus
  → [1] Check MessageId for duplicate (idempotency — root CLAUDE.md rule)
  → [2] Deserialise and validate payload
  → [3] Process business logic (with Polly retry on transient DB/internal errors)
  → [4a] Success → Complete message (remove from queue)
  → [4b] Transient failure → Abandon message (Service Bus re-delivers, up to MaxDeliveryCount)
  → [4c] Permanent failure → Dead-letter message with reason
```

#### Delivery and Dead-Letter Settings

| Setting | Value | Rationale |
|---------|-------|-----------|
| `MaxDeliveryCount` | 10 | 10 delivery attempts before auto-dead-lettering |
| `LockDuration` | 5 minutes | Sufficient for most processing; long-running consumers must renew |
| `MessageTTL` | 7 days | Messages not processed in 7 days are dead-lettered (not silently dropped) |
| `DeadLetterReason` | Always set | Consumers must provide a reason string when dead-lettering |

#### Idempotency

Every consumer checks whether the `MessageId` has already been processed before beginning work:

```csharp
// In base consumer — do not override
var alreadyProcessed = await _idempotencyStore.HasBeenProcessedAsync(message.MessageId, cancellationToken);
if (alreadyProcessed) { await args.CompleteMessageAsync(message); return; }
```

The idempotency store is a table in the operational database (`dbo.ProcessedMessages`) with a 72-hour TTL on entries (sufficient for Service Bus redelivery windows). **This check is implemented once in the base class and must not be bypassed.**

#### Dead-Letter Queue Monitoring and Remediation

Dead-letter queues are monitored via Azure Monitor alerts:

- Alert fires when DLQ depth > 0 for more than 15 minutes
- Alert target: platform operations team via `crm.platform` topic → notification service
- Runbook: `docs/runbooks/dead-letter-remediation.md` governs investigation and replay

**Replay mechanism:** A `scripts/replay-dlq.sh` script replays dead-lettered messages back to the source topic after the root cause is resolved. Replay is idempotent (idempotency store prevents double-processing). Replay must be a manual, supervised operation — never automated.

#### Poison Message Handling

A poison message is one that consistently fails processing (e.g. malformed payload that passes schema validation but fails business logic). After `MaxDeliveryCount` attempts, Service Bus automatically moves it to the DLQ. The consumer must:

- Dead-letter with a clear `DeadLetterReason` string (e.g. `"BusinessRuleViolation: Lead TenantId does not match authenticated tenant"`)
- Never silently swallow the error (root CLAUDE.md rule: never catch and swallow exceptions)
- Log the `MessageId`, `TenantId`, `CorrelationId`, and reason at `Warning` level (not PII)

---

### Saga Pattern — When to Use Orchestration vs Choreography

#### Use Choreography When:
- The operation is a single event triggering a single independent reaction in another service
- Failure of the reaction does not require compensating the original operation
- Example: `lead.created` → Marketing service sends a welcome email. If the email fails, retrying is sufficient; no compensation of the lead is needed.

#### Use Orchestration (Durable Functions Saga) When:
- The operation spans multiple services and must succeed or fail atomically (from a business perspective)
- A partial failure requires compensating already-completed steps
- The operation has human approval steps
- Example: Campaign launch (see below)

#### Identified Sagas (R1)

| Saga | Trigger | Services involved | Compensation needed |
|------|---------|------------------|-------------------|
| Campaign launch | `POST /api/v1/marketing/campaigns/{id}/launch` | Marketing, SFA (attribution setup), CS&S (auto-case rules), Analytics (event) | Yes — if SFA attribution fails, campaign must be rolled back to Draft |
| Lead conversion to opportunity | `POST /api/v1/sfa/leads/{id}/convert` | SFA, Marketing (remove from active journeys), Analytics | Yes — if journey removal fails, lead conversion rolls back |
| Tenant suspension | Billing event on `crm.platform` | Platform, Identity (access revoke), all modules | Yes — must be atomic; partial suspension is worse than none |
| Case escalation | SLA breach event on `crm.css` | CS&S, SFA (if case linked to opportunity), Notification | Partial — notification failure is non-compensating; SFA link failure rolls back escalation |

Each saga is implemented as a Durable Function orchestration in `src/functions/sagas/`. Each step is an activity function. Compensation activities are defined alongside the forward activities.

#### Saga Implementation Pattern

```csharp
// Orchestrator — in src/functions/sagas/CampaignLaunchSaga.cs
[Function(nameof(CampaignLaunchSaga))]
public static async Task RunOrchestrator(
    [OrchestrationTrigger] TaskOrchestrationContext context,
    CampaignLaunchInput input)
{
    var completedSteps = new List<string>();
    try
    {
        await context.CallActivityAsync(nameof(SetCampaignStatusLaunching), input);
        completedSteps.Add(nameof(SetCampaignStatusLaunching));

        await context.CallActivityAsync(nameof(SetupSfaAttribution), input);
        completedSteps.Add(nameof(SetupSfaAttribution));

        await context.CallActivityAsync(nameof(RegisterCssCampaignRules), input);
        completedSteps.Add(nameof(RegisterCssCampaignRules));

        await context.CallActivityAsync(nameof(SetCampaignStatusActive), input);
        // Publish crm.analytics event
    }
    catch (Exception ex)
    {
        // Compensate in reverse order
        foreach (var step in Enumerable.Reverse(completedSteps))
        {
            await context.CallActivityAsync($"Compensate_{step}", input);
        }
        await context.CallActivityAsync(nameof(SetCampaignStatusFailed), input with { Reason = ex.Message });
        throw;
    }
}
```

Sagas publish their terminal state (`saga.completed` or `saga.failed`) to the relevant Service Bus topic so interested services can react without polling.

---

### What is NOT Permitted

- **Retry loops inside business logic** — all retry is handled by Polly at the infrastructure layer. Business logic must not contain `while (retries < max)` constructs.
- **Swallowing `OperationCanceledException`** — if a `CancellationToken` is cancelled, the exception propagates. Do not catch it as a general exception.
- **Infinite retry** — all retry policies have a finite `MaxAttempts`. Infinite retry converts a transient failure into a hung process.
- **Cross-saga dependency** — one saga must not trigger another saga as a step. If business logic requires this, it is a sign the saga boundaries are wrong. Raise with Tech Director.

---

## Consequences

### Positive
- Polly policies defined once in `_template` — all services inherit consistent behaviour
- Idempotency check in base consumer class — cannot be accidentally omitted
- DLQ monitoring and replay give operational visibility and recovery capability
- Saga orchestration makes multi-step business operations visible, auditable, and compensatable
- Circuit breaker prevents a failing dependency from exhausting thread pools across the platform

### Negative
- Polly pipeline adds a small overhead per call — negligible for CRM workloads
- Durable Function sagas add implementation complexity vs simple event chains
- Per-tenant AI concurrency limit is in-memory per instance — not a global limit in R1
- DLQ replay is manual — requires operational discipline and runbook adherence
