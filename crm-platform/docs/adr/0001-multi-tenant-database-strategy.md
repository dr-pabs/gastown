# ADR 0001 — Multi-Tenant Database Strategy

**Status**: Accepted
**Date**: 2026-03-29
**Deciders**: Technical Director

## Context

The platform must support multiple tenants (SaaS model) with strong data isolation guarantees. Three options considered:

1. **Database per tenant** — maximum isolation, high cost, complex provisioning
2. **Schema per tenant** — good isolation, moderate cost, complex migrations
3. **Shared database with Row-Level Security** — lowest cost, scales well, requires careful implementation

## Decision

Use a **shared database** (Azure SQL Hyperscale) with:
- `TenantId` column on every table
- SQL Row-Level Security (RLS) policy enforced at database level
- EF Core global query filters as application-level enforcement (defence in depth)
- Tenant context set via `SESSION_CONTEXT` before each query

## Consequences

**Positive**:
- Single database to manage, monitor, and back up
- Efficient use of DTUs/vCores across tenants
- Straightforward migration strategy (single schema)

**Negative**:
- A bug in tenant isolation code could expose one tenant's data to another — mitigated by mandatory tenant isolation tests on every endpoint
- "Noisy neighbour" risk — mitigated by query timeouts and connection pooling limits per tenant

## Compliance

Tenant isolation is verified by the mandatory tenant isolation test on every API endpoint (see root CLAUDE.md). This test is a CI gate and cannot be bypassed.
