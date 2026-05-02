# ADR 0002 — Service-to-Service Communication via Service Bus

**Status**: Accepted
**Date**: 2026-03-29
**Deciders**: Technical Director

## Context

With 10+ microservices, cross-service communication must be reliable, decoupled, and auditable.

## Decision

All cross-service communication uses **Azure Service Bus** (topics/subscriptions). Direct HTTP calls between backend services are prohibited.

## Consequences

**Positive**:
- Services are loosely coupled — one service failing does not cascade synchronously to another
- Messages are durable — not lost if the consuming service is temporarily down
- Built-in dead-letter queue for debugging failed message processing
- Audit trail of all cross-module events

**Negative**:
- Eventual consistency — a caller cannot get an immediate synchronous response from another service
- Dead-letter queues must be monitored and processed
- More complex local development setup

## Exceptions

API Gateway (APIM) → backend services uses HTTP (this is a client→service boundary, not service→service).
