# Local Development Guide

> This is the companion guide to [ADR 0012 — Local Development Experience](adr/0012-local-development-experience.md).
> Read the ADR for the reasoning behind every decision here.

---

## Prerequisites

| Tool | Version | Notes |
| --- | --- | --- |
| .NET SDK | 8.0+ | `dotnet --version` |
| Node.js | 20 LTS | For both frontend portals |
| Docker Desktop | Latest | For local infrastructure |
| Azure CLI | Latest | `az --version` |
| EF Core tools | Latest | `dotnet tool install -g dotnet-ef` |
| `make` | Any | Pre-installed on macOS |

---

## First-Time Setup

### 1. Clone the repository

```bash
git clone https://github.com/dr-pabs/crm-platform
cd crm-platform
```

### 2. Log in to Azure

All local services use `DefaultAzureCredential` — your `az login` session is used automatically.

```bash
az login
az account set --subscription <dev-subscription-id>
```

### 3. Onboard your developer account

This script grants the necessary Azure RBAC roles on the dev subscription (Key Vault, Service Bus, Storage, App Configuration):

```bash
./scripts/onboard-developer.sh --email "you@company.com" --env dev
```

You need someone with `Owner` or `User Access Administrator` on the dev subscription to run this for you if you don't have it yourself.

After this script completes, a human with SQL admin access must also run:

```sql
CREATE USER [you@company.com] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [you@company.com];
ALTER ROLE db_datawriter ADD MEMBER [you@company.com];
```

### 4. Start local infrastructure

```bash
make dev-infra
```

This starts four Docker containers:
- **SQL Server 2022** on port `1433` — with RLS policy and seed data applied automatically
- **Azure Service Bus Emulator** on ports `5672` (AMQP) and `8080` (management UI)
- **Azurite** on ports `10000`/`10001`/`10002` (Blob / Queue / Table)
- **Mailpit** on ports `1025` (SMTP) and `8025` (web UI)

Wait for all containers to report healthy before continuing:

```bash
docker compose ps
```

### 5. Apply EF Core migrations

```bash
make migrate
```

This runs `dotnet ef database update` against the local SQL Server for all nine services.

### 6. Seed development data

The SQL init scripts run automatically when the SQL container first starts (`docker-entrypoint-initdb.d`). If you need to re-seed:

```bash
make seed
```

This re-runs `05-seed-dev-data.sql` only (tenants and users are not re-seeded — they already exist after `make dev-infra`).

---

## Daily Development

### Start the full stack

```bash
make dev
```

### Start only the service you're working on

```bash
make dev-sfa       # identity + SFA service
make dev-css       # identity + CS&S service
make dev-marketing # identity + marketing service
make dev-infra     # infrastructure only (no .NET services)
```

Using `make dev-{service}` rather than `make dev` saves memory and startup time when you only need one service.

### Stop everything

```bash
make stop
```

### Full reset (tear down and rebuild from scratch)

```bash
make reset
```

---

## Service Port Reference

| Service | Port |
| --- | --- |
| `identity-service` | 5001 |
| `platform-admin-service` | 5002 |
| `sfa-service` | 5010 |
| `css-service` | 5020 |
| `marketing-service` | 5030 |
| `analytics-service` | 5040 |
| `ai-orchestration-service` | 5050 |
| `auth-stub` (local only) | 5100 |
| Staff portal (React) | 3000 |
| SQL Server | 1433 |
| Service Bus emulator (AMQP) | 5672 |
| Service Bus emulator (Management UI) | 8080 |
| Azurite (Blob) | 10000 |
| Azurite (Queue) | 10001 |
| Azurite (Table) | 10002 |
| Mailpit (SMTP) | 1025 |
| Mailpit (Web UI) | 8025 |

---

## Authentication Locally

### Option 1 — Dev Azure credentials (preferred)

Use a real JWT from the dev Entra tenant. Pre-created test users:

| User | Role | Tenant |
| --- | --- | --- |
| `dev-salesrep-a@devtenant.onmicrosoft.com` | SalesRep | TenantA |
| `dev-manager-a@devtenant.onmicrosoft.com` | SalesManager | TenantA |
| `dev-admin-a@devtenant.onmicrosoft.com` | TenantAdmin | TenantA |
| `dev-salesrep-b@devtenant.onmicrosoft.com` | SalesRep | TenantB |
| `dev-customer-a@devb2ctenant.ciamlogin.com` | (Customer) | TenantA |

### Option 2 — Local auth stub (offline / fast iteration)

The auth stub runs at `http://localhost:5100` and issues signed JWTs for any tenant/role combination.

```bash
# Get a TenantA SalesRep token
./scripts/local/get-dev-token.sh --tenant TenantA --role SalesRep

# Get a TenantB Manager token
./scripts/local/get-dev-token.sh --tenant TenantB --role SalesManager
```

Set `DEV_AUTH_MODE=stub` in your environment to allow stub-issued tokens. This variable is **only** respected when `ASPNETCORE_ENVIRONMENT=Development` — a startup check enforces this.

> **Never** set `DEV_AUTH_MODE=stub` in any non-Development environment. The startup check will throw on boot.

---

## Running Tests

```bash
make test-unit          # Unit + domain tests only (no infrastructure required)
make test-integration   # Integration tests (requires make dev-infra to be running)
make test-local         # Both (unit + integration + tenant isolation)
```

Tenant isolation tests run as part of `test-local` and are the **most critical test category**. They use `[Trait("Category","TenantIsolation")]` and are filtered separately in CI.

> The EF Core in-memory provider is **prohibited** for all tests. All tests that touch data use the local SQL Server container. See root `CLAUDE.md`.

---

## Frontend Development

### Install dependencies

```bash
cd src/frontend/staff-portal && npm install
```

### Start portals

```bash
# Staff portal — http://localhost:3000
cd src/frontend/staff-portal && npm run dev
```

The staff portal proxies `/api` to `http://localhost:5000` (via `vite.config.ts`). Ensure the relevant backend services are running.

### Run frontend tests

```bash
cd src/frontend/staff-portal && npm test
```

---

## Azure Services Used Locally

Some services cannot be fully emulated and use the shared dev Azure subscription:

| Service | Local | Dev Azure |
| --- | --- | --- |
| SQL Server | ✅ Docker | ✅ Real Azure SQL (integration tests) |
| Service Bus | ✅ Emulator | ✅ Real Service Bus (E2E testing) |
| Storage | ✅ Azurite | — |
| Key Vault (`crm-dev-kv`) | — | ✅ Required |
| AI Foundry (`crm-dev-ai`) | — | ✅ Required (rate-limited) |
| Container Registry | — | ✅ For image builds |
| Entra ID (staff) | ⚠️ Auth stub | ✅ Dev Entra tenant |
| Entra External ID (customer) | ⚠️ Auth stub | ✅ Dev CIAM tenant |

Access to dev Azure services requires completing the [onboarding step](#3-onboard-your-developer-account) above.

---

## Viewing Local Emails

All outbound email (invitations, notifications, SLA alerts) is captured by Mailpit in local development. No real emails are sent.

Open the Mailpit web UI: **http://localhost:8025**

---

## Viewing Service Bus Messages

The Service Bus emulator management UI shows all topics, subscriptions, and message queues:

**http://localhost:8080**

This is useful for debugging Service Bus consumers and verifying event routing.

---

## Troubleshooting

### `make dev-infra` fails — containers not starting

```bash
docker compose down -v    # remove volumes
docker compose up -d      # start fresh
```

### SQL migrations fail — `connection refused`

The SQL container takes ~30 seconds to become healthy on first start. Wait for:

```bash
docker compose ps   # sql should show "healthy"
```

Then re-run `make migrate`.

### Auth returns 401 with stub token

Ensure `DEV_AUTH_MODE=stub` is set in your shell environment AND `ASPNETCORE_ENVIRONMENT=Development` is set. The stub issuer is only trusted in Development mode.

### `az login` credentials expire during a long session

```bash
az login   # re-authenticate
```

`DefaultAzureCredential` will automatically pick up the new session.

### Key Vault access denied

Verify your RBAC assignment was applied by `onboard-developer.sh`:

```bash
az role assignment list --assignee "you@company.com" --scope /subscriptions/<sub-id>/resourceGroups/crm-dev-rg
```

You should see `Key Vault Secrets User` in the output. If not, ask someone with `Owner` to re-run `onboard-developer.sh`.

---

## Related

- [ADR 0012 — Local Development Experience](adr/0012-local-development-experience.md)
- [ADR 0004 — Authentication and Authorisation](adr/0004-authentication-and-authorisation.md)
- [ADR 0002 — Service Communication via Service Bus](adr/0002-service-communication-via-service-bus.md)
- [Runbook: Dead Letter Queue Remediation](runbooks/dead-letter-remediation.md)
- Root `CLAUDE.md` — coding standards and architectural rules
