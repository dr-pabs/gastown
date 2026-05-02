# CRM Platform

A multi-tenant SaaS CRM platform with four modules: Sales Force Automation (SFA), Customer Service & Support (CS&S), Marketing Automation, and Analytics.

## Architecture

- **Backend**: C# .NET 8 microservices on Azure Container Apps
- **Frontend**: TypeScript / React staff portal on Azure Static Web Apps
- **Headless delivery**: customer/self-service and integration capability are delivered through APIs, events, and APIM-governed products
- **Infrastructure**: Azure PaaS, deployed via Bicep
- **AI**: Azure AI Foundry (Claude models)
- **Deployment**: SaaS (shared, multi-tenant) and client-hosted (dedicated Azure subscription)

## Repository Structure

```
src/services/          → .NET 8 microservices
src/functions/         → Azure Durable Functions (journey engine, SLA timers)
src/frontend/          → React/TypeScript staff application
infra/modules/         → Reusable Bicep modules
infra/platform/        → SaaS platform deployment
infra/client-hosted/   → Client-hosted deployment template
scripts/               → Operational scripts
docs/adr/              → Architecture Decision Records
.github/workflows/     → CI/CD pipelines
```

## Getting Started

See [docs/adr/README.md](docs/adr/README.md) for architecture decisions.

The current target architecture is defined in [ADR 0013](docs/adr/0013-headless-platform-and-retained-staff-portal.md).

All agents must read [CLAUDE.md](CLAUDE.md) before writing any code.

## Environments

| Environment | Purpose |
|---|---|
| dev | Developer iteration |
| test | CI/CD pipelines, integration tests |
| staging | Pre-production validation |
| prod | Live SaaS platform |
