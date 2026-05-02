# Runbook: Dead Letter Queue (DLQ) Remediation

**Severity:** P2 (service degradation) — escalate to P1 if DLQ depth > 1000 or growing rapidly  
**References:** [ADR 0010 — Resilience and Saga Patterns](../adr/0010-resilience-and-saga-patterns.md)  
**Owner:** Platform Engineering  
**Last reviewed:** 2026-04-12

---

## Overview

Dead letter queues (DLQ) accumulate messages that could not be processed after the maximum delivery count (default: 10 retries). This runbook covers:

1. How to detect and triage a DLQ backlog
2. Root cause investigation
3. Safe message replay after resolution
4. Escalation criteria

---

## 1. Detection

### Automated alerts

The drift-detection workflow (`.github/workflows/drift-detection.yml`) polls Service Bus metrics. Alerts fire when:
- Any subscription DLQ depth > **100 messages**
- DLQ depth grows for **3 consecutive 5-minute intervals**

Alerts are sent to the on-call channel in Teams.

### Manual check

```bash
# List DLQ depths for all subscriptions across all topics
az servicebus topic subscription list \
  --resource-group crm-prod-rg \
  --namespace-name crm-prod-sb \
  --topic-name crm.sfa \
  --query "[].{name:name, dlq:countDetails.deadLetterMessageCount}" \
  -o table

# Repeat for: crm.css, crm.marketing, crm.identity, crm.platform
```

Or check the Azure Portal: **Service Bus namespace → Topics → \<topic\> → Subscriptions → \<subscription\> → Dead-letter**

---

## 2. Triage

### Identify the affected subscription

Each service subscribes to its relevant topics with a named subscription. The table below maps subscription names to owning services:

| Topic | Subscription | Owning Service |
| --- | --- | --- |
| `crm.sfa` | `analytics-service` | analytics-service |
| `crm.sfa` | `notification-service` | notification-service |
| `crm.sfa` | `ai-orchestration-service` | ai-orchestration-service |
| `crm.css` | `analytics-service` | analytics-service |
| `crm.css` | `notification-service` | notification-service |
| `crm.css` | `ai-orchestration-service` | ai-orchestration-service |
| `crm.marketing` | `analytics-service` | analytics-service |
| `crm.marketing` | `notification-service` | notification-service |
| `crm.identity` | `notification-service` | notification-service |
| `crm.platform` | `analytics-service` | analytics-service |

### Peek at dead-lettered messages

```bash
# Peek the first 10 dead-lettered messages (non-destructive)
az servicebus topic subscription dead-letter message peek \
  --resource-group crm-prod-rg \
  --namespace-name crm-prod-sb \
  --topic-name crm.sfa \
  --subscription-name analytics-service \
  --count 10
```

Examine `deadLetterReason` and `deadLetterErrorDescription` in the message properties. Common reasons:

| Reason | Likely cause |
| --- | --- |
| `MaxDeliveryCountExceeded` | Transient error that didn't resolve within 10 retries |
| `MessageSizeExceeded` | Payload too large (> 256 KB) |
| `TTLExpiredException` | Message sat in queue past its time-to-live |
| Application exception in body | Bug in the consumer's `ProcessAsync` method |

---

## 3. Root Cause Investigation

### Check Application Insights

```kusto
// Find exceptions from the affected service during the DLQ accumulation window
exceptions
| where timestamp between (datetime(YYYY-MM-DD T00:00:00Z) .. datetime(YYYY-MM-DD T23:59:59Z))
| where cloud_RoleName == "analytics-service"   // replace with affected service
| summarize count() by outerMessage, innermostMessage
| order by count_ desc
```

```kusto
// Correlate with specific message IDs from the DLQ peek output
traces
| where message contains "<MessageId from DLQ>"
| order by timestamp asc
```

### Check service logs directly

```bash
az containerapp logs show \
  --resource-group crm-prod-rg \
  --name crm-analytics-service \
  --follow \
  --tail 200
```

### Common root causes and resolutions

| Root cause | Resolution |
| --- | --- |
| Transient Azure SQL connectivity | Verify SQL Managed Instance health; retry after resolution |
| Schema mismatch (event payload changed) | Deploy compatible consumer version first, then replay |
| Bug in `ProcessAsync` | Deploy fix, then replay |
| Downstream service unavailable | Restore downstream service first, then replay |
| Invalid message payload (malformed JSON) | Investigate publisher; dead-lettered messages cannot be fixed — discard and file incident |

> ⚠️ **Never replay messages before the root cause is resolved.** Replaying into a broken consumer will re-dead-letter all messages and increase DLQ depth.

---

## 4. Message Replay

### Prerequisites

- Root cause is confirmed resolved (fix is deployed or downstream is healthy)
- You have verified the consumer is processing new messages successfully (check a healthy subscription on the same topic)
- Change has been reviewed and approved in the incident channel

### Replay procedure

> Replay is **always a manual, supervised operation**. Automated replay is explicitly prohibited (ADR 0010).

```bash
# Replay all dead-lettered messages from a specific subscription back to the topic
./scripts/replay-dlq.sh \
  --resource-group crm-prod-rg \
  --namespace crm-prod-sb \
  --topic crm.sfa \
  --subscription analytics-service \
  --environment prod

# Dry run first (prints messages, does not move them)
./scripts/replay-dlq.sh \
  --topic crm.sfa \
  --subscription analytics-service \
  --environment prod \
  --dry-run
```

### Monitor during replay

Watch the DLQ depth in real time to confirm messages are being consumed (not re-dead-lettered):

```bash
watch -n 10 'az servicebus topic subscription show \
  --resource-group crm-prod-rg \
  --namespace-name crm-prod-sb \
  --topic-name crm.sfa \
  --name analytics-service \
  --query "countDetails" -o table'
```

If DLQ depth continues to grow during replay, **stop the replay immediately** and re-investigate.

### Replay is idempotent

All Service Bus consumers implement an idempotency store (`IIdempotencyStore`) keyed on `MessageId`. Replayed messages that were already successfully processed in a prior attempt will be silently skipped — there is no double-processing risk.

---

## 5. Post-Incident

1. Confirm DLQ depth returns to 0
2. Confirm no new messages are dead-lettering
3. Write a brief incident summary in the incident channel: root cause, time to detection, time to resolution, messages affected
4. If the root cause was a code bug, create a GitHub issue tagged `incident` with the fix PR linked
5. If the root cause was infrastructure (SQL, Service Bus), open an Azure support ticket if warranted

---

## 6. Escalation

| Condition | Action |
| --- | --- |
| DLQ depth > 1000 and growing | Escalate to P1, page on-call engineer |
| Root cause unknown after 30 minutes | Escalate to Tech Director |
| Messages contain PII that must not be replayed | Escalate to Data Protection Officer before any replay |
| DLQ accumulation on `crm.identity` topic | Treat as P1 immediately — identity events affect user provisioning |

---

## Related

- [ADR 0010 — Resilience and Saga Patterns](../adr/0010-resilience-and-saga-patterns.md)
- [ADR 0002 — Service Communication via Service Bus](../adr/0002-service-communication-via-service-bus.md)
- `scripts/replay-dlq.sh` — replay tool
- `drift-detection.yml` — automated DLQ monitoring workflow
