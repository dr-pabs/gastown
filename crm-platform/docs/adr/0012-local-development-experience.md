# ADR 0012 — Local Development Experience

**Status:** Accepted  
**Date:** 2026-04-04  
**Deciders:** Technical Director  
**Supersedes:** —  
**Superseded by:** —

---

## Context

The platform has architectural choices that make local development non-trivial:

- **Azure Service Bus** (ADR 0002) — no local emulator exists for Service Bus topics/subscriptions in the same way Azurite emulates Storage
- **Azure SQL with Row-Level Security** (ADR 0001) — `SESSION_CONTEXT` must be set per connection; the standard EF Core in-memory provider does not support this
- **Managed Identity** (root CLAUDE.md) — no connection strings with passwords; Managed Identity does not work outside Azure without special tooling
- **Multi-service architecture** — an agent working on the SFA service needs the identity service, platform service, and potentially the AI gateway running to test realistic scenarios
- **Two frontend portals** with different auth configurations

Without a defined local development experience, agents will:
- Use workarounds that bypass security controls (e.g. disabling auth middleware locally)
- Write integration tests against EF Core in-memory (explicitly prohibited in root CLAUDE.md)
- Be unable to reproduce production-like behaviour locally
- Spend disproportionate time on environment setup rather than feature development

This ADR defines:
1. The authoritative local development environment setup
2. How each architectural concern is handled locally
3. What runs locally vs what uses a shared dev Azure subscription
4. How agents start the platform locally with minimal friction

---

## Decision

**Use Docker Compose for local infrastructure (SQL Server, Service Bus emulator, Azurite). Use Azure DefaultAzureCredential with local developer credentials for Azure service access. Use a shared dev Azure subscription for services that cannot be emulated (real Azure SQL for integration tests, real Service Bus for end-to-end local testing). Provide a single `make dev` command that starts the full local stack.**

---

## Detail

### Local Infrastructure — Docker Compose

A `docker-compose.yml` at the repository root starts all locally-emulatable infrastructure:

```yaml
# docker-compose.yml (abbreviated)
services:

  sql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      SA_PASSWORD: "Dev_Password_123!"   # Local dev only — never used in any Azure env
      ACCEPT_EULA: "Y"
    ports:
      - "1433:1433"
    volumes:
      - sql-data:/var/opt/mssql
      - ./scripts/local/sql-init:/docker-entrypoint-initdb.d

  servicebus-emulator:
    image: mcr.microsoft.com/azure-messaging/servicebus-emulator:latest
    ports:
      - "5672:5672"   # AMQP
      - "8080:8080"   # Management
    volumes:
      - ./scripts/local/servicebus-config.json:/ServiceBus_Emulator/ConfigFiles/Config.json

  azurite:
    image: mcr.microsoft.com/azure-storage/azurite:latest
    ports:
      - "10000:10000"  # Blob
      - "10001:10001"  # Queue
      - "10002:10002"  # Table

  mailpit:
    image: axllent/mailpit:latest
    ports:
      - "1025:1025"   # SMTP
      - "8025:8025"   # Web UI — view outbound emails locally

volumes:
  sql-data:
```

**Microsoft's Azure Service Bus Emulator** (`mcr.microsoft.com/azure-messaging/servicebus-emulator`) supports topics and subscriptions, making it suitable for the platform's pub/sub patterns. The `servicebus-config.json` pre-creates all required topics (`crm.sfa`, `crm.css`, `crm.marketing`, `crm.identity`, `crm.platform`, `crm.analytics`) with their subscriptions. This file lives in `scripts/local/` and is version-controlled.

**SQL Server local** uses a password (`Dev_Password_123!`) only in local Docker Compose. This is the **only** place in the repository a SQL password appears, and it is in a local-only context. No Azure environment uses a password — they all use Managed Identity. The password is not a secret — it is a well-known local dev convention and is committed to the repository.

**Mailpit** captures outbound SMTP in a local web UI (port 8025). This means agents can test email-sending features (invitations, notifications, reports) without a real mail server.

### SQL Server — RLS and SESSION_CONTEXT Locally

The local SQL Server container runs the same RLS policy scripts as production. The `scripts/local/sql-init/` directory contains:

```
01-create-database.sql       → Creates CrmPlatform database
02-apply-rls-policy.sql      → Applies SESSION_CONTEXT RLS policy (same script as prod)
03-seed-dev-tenants.sql      → Seeds two dev tenants: TenantA (Guid) and TenantB (Guid)
04-seed-dev-users.sql        → Seeds dev users for each tenant with roles
05-seed-dev-data.sql         → Seeds representative CRM data (leads, cases, campaigns)
```

These scripts run automatically when the SQL container first starts (via `docker-entrypoint-initdb.d`).

The EF Core `DbContext` in local development sets `SESSION_CONTEXT` via the same `ITenantContext` mechanism as production — there is no local bypass. This is enforced in the `_template` `DbContext` configuration. The `ITenantContext` in local dev is populated from:
- A development JWT (issued by a local identity stub — see Auth section below)
- Or an environment variable `DEV_TENANT_ID` for non-HTTP contexts (background services, local scripts)

**EF Core in-memory provider is prohibited** (root CLAUDE.md). There are no exceptions. All local development that touches data uses the local SQL Server container.

### Authentication Locally

Production uses Entra ID and Entra External ID (ADR 0004), neither of which is available without an Azure subscription. Local development uses two alternatives:

#### Option 1 — Dev Azure Subscription (Preferred for realistic testing)

Agents authenticate to a real Entra ID tenant in the shared dev Azure subscription using their own developer credentials (`az login`). The dev Entra tenant has pre-created test users for each tenant and role combination:

| User | Role | Tenant |
|------|------|--------|
| `dev-salesrep-a@devtenant.onmicrosoft.com` | SalesRep | TenantA |
| `dev-manager-a@devtenant.onmicrosoft.com` | SalesManager | TenantA |
| `dev-admin-a@devtenant.onmicrosoft.com` | TenantAdmin | TenantA |
| `dev-salesrep-b@devtenant.onmicrosoft.com` | SalesRep | TenantB |
| `dev-customer-a@devb2ctenant.ciamlogin.com` | (Customer) | TenantA |

These users issue real JWTs against the dev Entra tenant. The local service validates them against the dev Entra JWKS endpoint. This is the closest to production behaviour.

#### Option 2 — Local JWT Stub (For offline / fast iteration)

A lightweight ASP.NET Core application (`src/services/_local/auth-stub/`) issues signed JWTs with configurable claims. It is **not** a real identity provider — it exists only for local development and is never deployed to any environment.

The auth stub:
- Runs on `http://localhost:5100`
- Issues JWTs signed with a local development key (not a production key)
- Accepts a simple HTTP POST with `tenantId`, `userId`, `role` as a body and returns a signed JWT
- Is configured as a trusted issuer only in `appsettings.Development.json` — never in `appsettings.json` or any non-development config

A helper script (`scripts/local/get-dev-token.sh`) wraps the auth stub for convenience:

```bash
# Get a TenantA SalesRep token
./scripts/local/get-dev-token.sh --tenant TenantA --role SalesRep
```

Agents use the stub token in API calls and in running services via the `DEV_AUTH_MODE=stub` environment variable. When `DEV_AUTH_MODE=stub`, the service's auth middleware accepts stub-issued tokens in addition to real Entra tokens.

**`DEV_AUTH_MODE=stub` is blocked from being set in any environment other than `Development`** by a startup validation check in the `_template` service base.

### Managed Identity Locally

Azure services accessed via Managed Identity in production (Key Vault, Service Bus, Storage) are accessed locally via `DefaultAzureCredential`, which in a local context uses the developer's `az login` credentials in priority order:
1. Environment variables (`AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_TENANT_ID`) — for CI
2. Workload Identity (in cluster)
3. Azure CLI credentials (`az login`) — **this is what local development uses**
4. Visual Studio / VS Code credentials

Agents run `az login` once. After that, all `DefaultAzureCredential` calls in local services use their Azure CLI session automatically. No additional configuration is needed.

For Key Vault: local services connect to the **dev subscription Key Vault** (`crm-dev-kv`). Dev secrets are real Azure Key Vault entries (not production secrets, but real Key Vault). This means agents need `Key Vault Secrets User` role assignment on the dev Key Vault — provisioned as part of onboarding.

For Service Bus: local services connect to the **local Docker emulator** by default (connection string `Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=...` is set in `appsettings.Development.json`). This is the only connection string with a key that appears in config files — it is the emulator's well-known local key, not a production secret.

### Starting the Full Stack

A `Makefile` at the repository root provides the primary developer interface:

```makefile
dev:           ## Start full local stack (infrastructure + all services)
	docker compose up -d
	dotnet run --project src/services/identity-service &
	dotnet run --project src/services/platform-service &
	dotnet run --project src/services/sfa-service &
	dotnet run --project src/services/css-service &
	dotnet run --project src/services/marketing-service &
	dotnet run --project src/services/analytics-service &
	dotnet run --project src/services/ai-gateway &
	dotnet run --project src/services/staff-bff &
	cd src/frontend && pnpm dev

dev-sfa:       ## Start only infrastructure + identity + SFA service (for SFA feature work)
	docker compose up -d
	dotnet run --project src/services/identity-service &
	dotnet run --project src/services/sfa-service

dev-infra:     ## Start infrastructure only (SQL, Service Bus, Azurite, Mailpit)
	docker compose up -d

test-local:    ## Run all tests against local infrastructure
	dotnet test src/ --filter "Category!=Integration"
	dotnet test src/ --filter "Category=Integration" --environment Development

migrate:       ## Apply EF Core migrations to local SQL
	dotnet ef database update --project src/services/sfa-service
	dotnet ef database update --project src/services/css-service
	# ... etc

seed:          ## Re-seed development data
	sqlcmd -S localhost -U sa -P Dev_Password_123! -i scripts/local/sql-init/05-seed-dev-data.sql

reset:         ## Tear down and rebuild local environment from scratch
	docker compose down -v
	docker compose up -d
	make migrate
	make seed

stop:          ## Stop all local services
	docker compose down
	pkill -f "dotnet run" || true
```

Agents working on a single service use `make dev-sfa` (or equivalent) rather than `make dev`. This avoids running services they don't need and reduces local resource consumption.

### Service-Specific Local Ports

| Service | Local port |
|---------|-----------|
| `identity-service` | 5001 |
| `platform-service` | 5002 |
| `sfa-service` | 5010 |
| `css-service` | 5020 |
| `marketing-service` | 5030 |
| `analytics-service` | 5040 |
| `ai-gateway` | 5050 |
| `staff-bff` | 5060 |
| `auth-stub` | 5100 |
| Staff portal (React) | 3000 |
| Customer portal (React) | 3001 |
| SQL Server | 1433 |
| Service Bus emulator (AMQP) | 5672 |
| Service Bus emulator (Mgmt) | 8080 |
| Azurite (Blob) | 10000 |
| Mailpit (SMTP) | 1025 |
| Mailpit (Web UI) | 8025 |

All ports are documented in `docs/local-development.md` (the companion operational guide to this ADR).

### What Uses the Dev Azure Subscription

Some services cannot be fully emulated locally and use the shared dev Azure subscription:

| Service | Local emulation | Dev Azure |
|---------|----------------|-----------|
| Azure SQL | ✅ Docker SQL Server | ✅ Real Azure SQL (integration tests only) |
| Azure Service Bus | ✅ Service Bus emulator | ✅ Real Service Bus (E2E local testing) |
| Azure Storage | ✅ Azurite | ❌ Not needed |
| Azure Key Vault | ❌ Not emulated | ✅ `crm-dev-kv` |
| Azure AI Foundry | ❌ Not emulated | ✅ `crm-dev-ai` (rate-limited) |
| Azure Container Registry | ❌ Not emulated | ✅ `crmdevacr` (for image builds) |
| Entra ID (staff auth) | ⚠️ Auth stub (Option 2) | ✅ Dev Entra tenant (Option 1) |
| Entra External ID (customer auth) | ⚠️ Auth stub (Option 2) | ✅ Dev External ID tenant (Option 1) |

Agents must be granted access to the dev subscription as part of onboarding. The `scripts/onboard-developer.sh` script assigns the necessary RBAC roles (Key Vault Secrets User, Service Bus Data Owner on dev namespace, AI Foundry User).

### Configuration Files

`appsettings.Development.json` (committed to repository) contains **non-secret** local configuration:
- Service Bus emulator connection string (well-known local key)
- Local SQL Server connection string (well-known local password)
- Auth stub issuer URL
- Local service port assignments
- Feature flags for development (e.g. `"AI:UseMockResponses": false`)

`appsettings.Development.json` must **never** contain:
- Production or staging connection strings
- Real Entra client secrets
- Any value that would be a secret in a non-local context

Secrets consumed from Key Vault in all environments including local dev (via `az login` → `DefaultAzureCredential`):
- Azure AI Foundry endpoint and API key
- Any third-party service credentials

### AI Gateway Locally

The `ai-gateway` connects to the **dev Azure AI Foundry** instance (`crm-dev-ai`). This is a real AI endpoint with rate limits appropriate for development (lower token quota than production).

Agents working on features that call the AI gateway do not need to mock the AI. They call the real dev AI endpoint. This gives realistic latency and response quality during development.

If an agent needs deterministic AI responses for testing (e.g. unit testing a feature that depends on a specific AI response), they use the `IAiGatewayClient` interface with a mock implementation in their test project. The mock is configured in the test's DI setup — not by modifying the gateway itself.

---

## Consequences

### Positive
- `make dev` starts the full stack — minimal friction for agents to begin work
- No bypasses to auth or tenant isolation in local development — agents see the same behaviour as production
- Service Bus emulator supports full topic/subscription model — realistic event-driven testing locally
- Auth stub enables offline development without Azure subscription dependency
- `make dev-{service}` allows targeted startup — agents working on SFA don't run marketing and analytics services
- Dev Azure Key Vault means agents always use real secret management — no `appsettings` hacks

### Negative
- Docker is a prerequisite — agents must have Docker Desktop or equivalent installed
- First-time setup requires `az login` and `scripts/onboard-developer.sh` — ~15 minutes of initial setup
- Dev Azure subscription access means onboarding has an Azure RBAC step requiring a human with Owner rights
- AI Foundry dev instance has rate limits — agents doing heavy AI feature development may hit quota; solution is to use mock for unit tests and real for manual testing only
- The auth stub introduces a local-only code path that must be maintained and kept in sync with real auth behaviour
