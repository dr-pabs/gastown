# ADR 0007 — AI Integration Pattern

**Status:** Accepted  
**Date:** 2026-04-04  
**Deciders:** Technical Director  
**Supersedes:** —  
**Superseded by:** —

---

## Context

The platform uses Azure AI Foundry (Claude models) to provide AI-assisted capabilities across modules. Anticipated use cases include:

- **SFA:** Lead scoring explanations, email draft generation, next-best-action suggestions
- **CS&S:** Case summarisation, suggested reply generation, sentiment analysis on incoming tickets
- **Marketing:** Journey copy generation, audience segment description, campaign performance narrative
- **Analytics:** Natural language querying ("show me leads from last quarter by region")

Without a shared architectural pattern, each service or feature team will implement AI calls independently, resulting in:
- Inconsistent retry and timeout behaviour
- PII leaking into prompts (violating root CLAUDE.md)
- No visibility into AI token consumption per tenant
- No ability to rate-limit or throttle AI use per tenant or plan tier
- Inconsistent prompt versioning — a prompt change in one service is invisible to others
- No fallback strategy when the AI endpoint is unavailable

The following must be resolved:

1. Is AI called from individual microservices, or through a dedicated gateway?
2. How are prompts managed and versioned?
3. How is PII excluded from AI calls?
4. How are AI failures handled?
5. How is AI token consumption tracked per tenant?

---

## Options Considered

### Option A — Direct calls from each microservice
Each service that needs AI imports an SDK and calls Azure AI Foundry directly. Simple, no added infrastructure. Rejected — the concerns listed above (PII, throttling, observability, prompt governance) cannot be solved consistently without a shared layer.

### Option B — Dedicated AI Gateway service
A single `ai-gateway` microservice acts as the internal broker for all AI calls. Other services call the gateway via Service Bus (async) or HTTP (sync, internal). The gateway owns: prompt rendering, PII scrubbing, token tracking, retry logic, and circuit breaking. This is the correct separation of concerns.

### Option C — AI logic in Azure Durable Functions
AI calls orchestrated as Durable Function activities. Good for long-running or multi-step AI workflows (e.g. multi-turn reasoning). Can be combined with Option B — the gateway handles single calls; Durable Functions handle multi-step AI workflows that require state.

---

## Decision

**Adopt Option B as the primary pattern (dedicated `ai-gateway` service) with Option C for multi-step AI workflows.**

---

## Detail

### AI Gateway Service

A dedicated `ai-gateway` service is deployed on Container Apps (internal ingress — not exposed externally via APIM). It is the **only** service that holds the Azure AI Foundry connection configuration and may communicate with external AI endpoints.

**No other service may call Azure AI Foundry directly.** This is an architectural rule enforced at:
- Code review (PR checklist item)
- Network level — Container Apps VNet policy blocks outbound AI API calls from all services except `ai-gateway`

#### Responsibilities

| Responsibility | Detail |
|---------------|--------|
| Prompt rendering | Loads prompt templates by ID + version, merges with caller-provided variables |
| PII scrubbing | Strips recognised PII patterns before sending to AI model (see PII section below) |
| Token accounting | Records prompt + completion token counts per `TenantId` and `PromptId` |
| Retry & timeout | Exponential backoff with jitter, max 3 retries, 30s timeout per call |
| Circuit breaking | Opens circuit after 5 consecutive AI endpoint failures; half-opens after 60s |
| Response caching | Caches deterministic responses (where `temperature=0`) by prompt hash + variable hash, TTL 5 minutes |
| Audit logging | Logs every AI call: `TenantId`, `PromptId`, `PromptVersion`, token counts, latency — no prompt content or response content in logs |

#### API Surface (internal HTTP, via service mesh)

```
POST /ai/complete
{
  "promptId": "sfa.lead-score-explanation",
  "promptVersion": "1.2",
  "tenantId": "{guid}",
  "correlationId": "{guid}",
  "variables": {
    "leadScore": 72,
    "leadSource": "webform",
    "industry": "retail"
  },
  "options": {
    "maxTokens": 500,
    "temperature": 0.3
  }
}
```

The gateway returns:

```
{
  "content": "...",
  "promptTokens": 312,
  "completionTokens": 187,
  "cached": false,
  "gatewayRequestId": "{guid}"
}
```

Callers receive a typed response — they never handle raw AI API responses.

### Communication Pattern — Sync vs Async

| Use case | Pattern | Rationale |
|----------|---------|-----------|
| User-facing, latency-sensitive (e.g. "suggest a reply" button) | Synchronous HTTP to `ai-gateway` (internal) | User is waiting — must be fast |
| Background enrichment (e.g. lead score decay, batch sentiment) | Async via Service Bus → `ai-gateway` subscriber | User not waiting — throughput over latency |
| Multi-step reasoning (e.g. campaign brief → audience → copy → review) | Durable Function orchestration → `ai-gateway` HTTP per step | Stateful, long-running, resumable |

Synchronous HTTP between services is **only** permitted for `caller → ai-gateway` calls. This is the second explicit exception to ADR 0002's Service Bus rule (the first being BFF → services per ADR 0005). Both exceptions are documented and governed.

### Prompt Management

Prompts are **not** stored in code. They are stored in the `dbo.Prompts` table, managed by the `ai-gateway` service.

| Column | Type | Notes |
|--------|------|-------|
| `PromptId` | `nvarchar(100)` | Namespaced: `{module}.{feature}` e.g. `sfa.lead-score-explanation` |
| `Version` | `nvarchar(20)` | Semantic: `1.0`, `1.1`, `2.0` |
| `Status` | `nvarchar(20)` | `Draft`, `Active`, `Deprecated` |
| `SystemPrompt` | `nvarchar(max)` | System message content |
| `UserPromptTemplate` | `nvarchar(max)` | Handlebars-style template with `{{variable}}` placeholders |
| `Model` | `nvarchar(100)` | Target model identifier (e.g. `claude-sonnet-4-20250514`) |
| `MaxTokens` | `int` | Hard cap for this prompt |
| `Temperature` | `decimal(3,2)` | Default temperature |
| `CreatedAt` | `datetime2` | |
| `CreatedBy` | `nvarchar(100)` | |

**Only one prompt version per `PromptId` may have `Status = Active` at a time.** Callers that specify a version receive that exact version. Callers that omit version receive the Active version. This allows prompt updates without code deployments, and rollback by setting the previous version to Active.

Prompt changes are **not** deployments — they are data changes reviewed and approved via a prompt review process (to be defined in a runbook). This process must include a test run against representative inputs before setting status to Active.

### PII Scrubbing

The AI gateway applies a PII scrubbing pass to all variable values before prompt rendering.

Patterns scrubbed (replaced with `[REDACTED]`):

| Pattern | Example |
|---------|---------|
| Email addresses | `user@example.com` |
| UK/international phone numbers | `+44 7700 900000` |
| Names (NER-based, best-effort) | `John Smith` |
| Credit card numbers | `4111 1111 1111 1111` |
| National ID patterns (NI, SSN, etc.) | `AB 12 34 56 C` |

**Callers are responsible for not passing PII-bearing fields as variables.** The PII scrubber is a defence-in-depth safeguard, not the primary control. The `ai-gateway` service CLAUDE.md will specify which variable types are permissible.

If a variable value is scrubbed, the gateway logs a warning (including `TenantId`, `PromptId`, and the variable name — not the value) and proceeds. It does not fail the request, as scrubbing is best-effort.

### Per-Tenant Token Governance

The `dbo.TenantAiUsage` table records token consumption:

| Column | Notes |
|--------|-------|
| `TenantId` | FK to `dbo.Tenants` |
| `Month` | `YYYY-MM` period |
| `PromptId` | Which prompt was called |
| `PromptTokens` | Cumulative this month |
| `CompletionTokens` | Cumulative this month |

The `platform-service` exposes usage data for billing. Plan-level token limits are enforced by the gateway before each call — if a tenant's monthly usage exceeds their plan limit, the gateway returns HTTP 429 to the caller with a structured error body. The caller surfaces this to the user as a soft limit message, not an unhandled error.

### Failure Handling

| Failure scenario | Gateway behaviour | Caller behaviour |
|-----------------|------------------|-----------------|
| AI endpoint timeout (>30s) | Returns `503` with `Retry-After` header | Surface "AI temporarily unavailable" — do not crash the feature |
| Circuit open | Returns `503` immediately | Same as above |
| PII scrubber strips all content | Returns `422` with detail | Log warning, surface "unable to generate" — do not retry |
| Plan token limit exceeded | Returns `429` with `TenantId` and limit info | Surface usage limit message to user |
| Model returns empty content | Returns `502` | Retry once, then surface error |

**AI features must degrade gracefully.** A failed AI call must never break a non-AI workflow. For example: if "suggest a reply" fails, the agent can still type a reply manually. AI is an enhancement, not a dependency for core workflows.

This is enforced at code review: any feature using AI must have a visible fallback path in the UI and a test for the `503` scenario.

### Multi-Step AI Workflows (Durable Functions)

For workflows requiring multiple AI calls with state between them (e.g. a campaign brief that goes through audience suggestion → copy generation → compliance check → approval), an Azure Durable Function orchestration is used.

Each step calls `ai-gateway` via HTTP activity. The orchestration handles:
- Step sequencing and conditional branching
- Human-in-the-loop approval steps (using Durable Functions' external event pattern)
- Retry of individual steps without restarting the whole workflow

These workflows are defined in `src/functions/` alongside the journey engine and SLA timers.

---

## Consequences

### Positive
- Single enforcement point for PII, token accounting, retry logic, and circuit breaking
- Prompt changes do not require code deployments — faster iteration, lower risk
- AI token consumption is visible per tenant — enables usage-based billing
- AI failures are contained — callers receive structured errors, not raw AI API failures
- Network policy enforces the architectural rule — no service can bypass the gateway

### Negative
- `ai-gateway` is a new service to build, test, and operate
- Synchronous AI calls add latency through the gateway layer (~10–20ms overhead) — acceptable for a CRM workload
- Prompt database requires its own review/approval process — overhead for non-technical prompt authors
- PII scrubbing is best-effort — callers must be educated not to pass PII in variables

---

## Model Configuration (R1)

| Model alias | Azure AI Foundry deployment | Use cases |
|------------|---------------------------|-----------|
| `standard` | Claude Sonnet (latest stable) | Most features — balanced cost/quality |
| `fast` | Claude Haiku (latest stable) | High-volume, low-latency needs (e.g. sentiment tagging) |

Model aliases are used in prompt records (`Model` column). Actual deployment names are configured in `ai-gateway` via Key Vault — never hardcoded. Model upgrades are a configuration change, not a code change.
