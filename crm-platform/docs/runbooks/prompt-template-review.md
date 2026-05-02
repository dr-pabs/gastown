# Runbook: Prompt Template Review and Promotion

**Severity:** N/A — planned operation  
**References:** [ADR 0007 — AI Integration Pattern](../adr/0007-ai-integration-pattern.md)  
**Owner:** Platform Engineering + Product  
**Last reviewed:** 2026-04-12

---

## Overview

Prompt templates are stored in the database (managed by `ai-orchestration-service`) and drive all AI-generated content: email drafts, case summaries, lead scoring rationale, and SMS messages. Changing a prompt template is a **data change**, not a code deployment, but it requires the same level of review as a code change because it directly affects output quality and compliance.

This runbook defines the review and promotion process for prompt template changes.

---

## When to Use This Runbook

Use this runbook when:

- Creating a new prompt template for a new AI capability
- Modifying the body, system prompt, or output schema of an existing template
- Deactivating a template (disabling an AI feature)
- Rolling back a template after a quality issue in production

---

## Roles

| Role | Responsibility |
| --- | --- |
| **Author** | Writes the template change and runs evaluation |
| **Reviewer** | Product Manager or Tech Lead — approves quality and tone |
| **Approver** | Tech Director — final approval before production promotion |

---

## 1. Create or Edit the Template

### Via the staff portal

Navigate to **Settings → Prompt Templates** in the staff portal. All template changes made here write to the database with `IsActive = false` by default — no live traffic is affected until the template is explicitly activated.

### Via API (for automation or bulk changes)

```bash
# Create a new template
curl -X POST https://<api-url>/ai/prompt-templates \
  -H "Authorization: Bearer <staff-token>" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Lead Outreach Email v2",
    "capability": "EmailDraft",
    "systemPrompt": "You are a professional sales assistant...",
    "userPromptTemplate": "Write an outreach email for lead: {{leadSummary}}",
    "outputFormat": "PlainText",
    "maxTokens": 500
  }'

# Edit an existing template (creates a new inactive version)
curl -X PUT https://<api-url>/ai/prompt-templates/<template-id> \
  -H "Authorization: Bearer <staff-token>" \
  -H "Content-Type: application/json" \
  -d '{ "userPromptTemplate": "Updated prompt text..." }'
```

---

## 2. Evaluation Against Representative Inputs

Before any template is activated, it must be tested against **at least 10 representative inputs** from the target capability.

### Obtain test inputs

```bash
# Export representative inputs from the dev or staging database
# (never use production data for evaluation)
./scripts/export-ai-test-inputs.sh \
  --capability EmailDraft \
  --count 20 \
  --environment dev \
  --output /tmp/email-draft-test-inputs.json
```

### Run evaluation

```bash
# Run the template against all test inputs and capture outputs
curl -X POST https://<api-url>/ai/prompt-templates/<template-id>/evaluate \
  -H "Authorization: Bearer <staff-token>" \
  -H "Content-Type: application/json" \
  -d @/tmp/email-draft-test-inputs.json \
  > /tmp/email-draft-evaluation-results.json
```

### Review outputs

Open `/tmp/email-draft-evaluation-results.json` and review each output for:

- [ ] Output is in the expected format (plain text / JSON / structured)
- [ ] No hallucinated facts (names, dates, figures that aren't in the input)
- [ ] Tone is appropriate for the capability (professional, helpful, concise)
- [ ] No PII leakage from other test inputs (cross-contamination check)
- [ ] Output length is within the expected range
- [ ] Edge cases handled correctly (empty input, very short input, non-English input if applicable)

Attach the evaluation results JSON to the review request.

---

## 3. Review Request

Create a GitHub issue using the `prompt-template-review` label with:

- **Template name** and **capability**
- **What changed** and **why**
- Link to the evaluation results file (attached or linked from a PR)
- **Reviewer** and **Approver** tagged

The Reviewer approves quality and tone. The Approver (Tech Director) gives final sign-off.

**Do not activate the template until both approvals are recorded on the GitHub issue.**

---

## 4. Activation

Once both approvals are obtained:

### Via the staff portal

Navigate to **Settings → Prompt Templates → \<template\>** and click **Activate**.

Only one template per capability can be active at a time. Activating a new template automatically deactivates the previous one. The deactivated template is retained in the database with `IsActive = false` for rollback purposes.

### Via API

```bash
curl -X POST https://<api-url>/ai/prompt-templates/<template-id>/activate \
  -H "Authorization: Bearer <staff-token>"
```

### Post-activation verification

After activating in **staging** first, verify:

```bash
# Trigger a test AI job for the affected capability
curl -X POST https://<staging-api-url>/ai/jobs \
  -H "Authorization: Bearer <staff-token>" \
  -H "Content-Type: application/json" \
  -d '{
    "capability": "EmailDraft",
    "entityType": "Lead",
    "entityId": "<dev-lead-id>"
  }'

# Check the job result
curl https://<staging-api-url>/ai/jobs/<job-id> \
  -H "Authorization: Bearer <staff-token>"
```

Only promote to production after staging verification passes.

---

## 5. Production Promotion

Promote the same template ID (not a new one) to production:

```bash
# Activate the same template in production
curl -X POST https://<prod-api-url>/ai/prompt-templates/<template-id>/activate \
  -H "Authorization: Bearer <prod-staff-token>"
```

Record the activation time and template ID in the GitHub issue.

---

## 6. Rollback

If the activated template produces unacceptable output in production:

### Immediate rollback

```bash
# Re-activate the previous template (it is still in the database with IsActive = false)
# Get the list of inactive templates for the capability
curl https://<prod-api-url>/ai/prompt-templates?capability=EmailDraft&isActive=false \
  -H "Authorization: Bearer <prod-staff-token>"

# Activate the previous version
curl -X POST https://<prod-api-url>/ai/prompt-templates/<previous-template-id>/activate \
  -H "Authorization: Bearer <prod-staff-token>"
```

Rollback does not require a code deployment and takes effect immediately.

### Post-rollback

- Record the rollback in the GitHub issue
- Re-run evaluation with a broader set of test inputs to understand the failure
- Do not re-activate the rolled-back template without a new review cycle

---

## Template Versioning and Retention

- Templates are **never hard-deleted**. Deactivated templates are retained indefinitely for audit and rollback.
- Each template has a `Version` integer that increments on every update.
- The `ai-orchestration-service` logs the `TemplateId` and `Version` with every AI job result — this allows full audit traceability of which prompt produced which output.

---

## Related

- [ADR 0007 — AI Integration Pattern](../adr/0007-ai-integration-pattern.md)
- Staff portal: **Settings → Prompt Templates**
- `ai-orchestration-service` — owns the `PromptTemplate` entity and activation API
- `src/services/ai-orchestration-service/Domain/Entities/PromptTemplate.cs`
