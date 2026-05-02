# ADR 0011 — CI/CD Pipeline Architecture

**Status:** Accepted  
**Date:** 2026-04-04  
**Deciders:** Technical Director, DevOps Engineer  
**Supersedes:** —  
**Superseded by:** —

---

## Context

The platform is a monorepo containing 10+ backend microservices, 2 frontend applications, Azure Functions, and Bicep infrastructure. CI/CD pipelines must:

- Build and test only what has changed (a change to the SFA service must not rebuild the CS&S service)
- Enforce the 80% coverage gate, tenant isolation tests, and all CLAUDE.md quality rules
- Progress changes through `dev → test → staging → prod` with appropriate gates at each environment
- Support the dual deployment model — SaaS (platform subscription) and client-hosted (client subscription)
- Handle secrets securely — no credentials in pipeline YAML or GitHub repository
- Allow agent-authored code to be built, validated, and deployed without manual intervention

The following must be resolved:

1. Monorepo change detection — how to build only what changed
2. Pipeline structure — per-service pipelines vs shared pipeline with path filtering
3. Deployment strategy — rolling, blue/green, or canary
4. Secret management in pipelines
5. Client-hosted update delivery
6. Quality gates per environment

---

## Options Considered

### Change Detection
- **Turborepo (for frontend) + custom path filters (for backend)** — Turborepo handles frontend incremental builds natively. GitHub Actions path filters (`on.push.paths`) handle backend service isolation.
- **Build everything on every commit** — simple but slow and wasteful. A change to a README would rebuild all 10+ services. Rejected.
- **Nx monorepo tooling** — comprehensive but adds significant tooling overhead for a primarily .NET + small React monorepo. Rejected.

### Deployment Strategy
- **Rolling deployment** — Container Apps default. New instances replace old ones gradually. Zero-downtime for stateless services. Simple.
- **Blue/green** — two identical environments, traffic switched atomically. Higher cost (double the running instances during switch). Useful for risky releases.
- **Canary** — gradual traffic shift to new version. Requires traffic splitting capability and meaningful metrics to validate. Complex to operate for a CRM (not a high-volume consumer product). Deferred to R2.

---

## Decision

**Use GitHub Actions with path-filtered per-service workflows for backend services and Turborepo for frontend. Rolling deployment for all services in R1. Blue/green as an opt-in for high-risk releases. All secrets via Azure Key Vault referenced by Managed Identity — no GitHub Secrets for Azure credentials.**

---

## Detail

### Repository Pipeline Structure

```
.github/workflows/
├── ci-backend-{service}.yml      → One per microservice (path-filtered)
├── ci-frontend.yml               → Single workflow, Turborepo handles affected detection
├── ci-functions.yml              → Azure Functions (path-filtered to src/functions/)
├── ci-infra.yml                  → Bicep validation and what-if (path-filtered to infra/)
├── cd-platform.yml               → Deploy to SaaS platform environments (dev/test/staging/prod)
├── cd-client-hosted.yml          → Package and publish client-hosted deployment template
├── quality-gate.yml              → Reusable workflow: coverage, lint, tenant isolation tests
└── pr-checks.yml                 → Runs on every PR: lint, build, unit tests, coverage check
```

### Backend Service CI Pipeline (ci-backend-{service}.yml)

Each microservice has its own workflow file. They are structurally identical — generated from a template. The only difference between them is the path filter and service name.

```yaml
on:
  push:
    paths:
      - 'src/services/sfa-service/**'
      - 'src/services/_template/**'   # Template changes trigger all services
      - '.github/workflows/quality-gate.yml'
  pull_request:
    paths:
      - 'src/services/sfa-service/**'
```

**Steps:**

| Step | Tool | Fail behaviour |
|------|------|---------------|
| Restore dependencies | `dotnet restore` | Fail pipeline |
| Build | `dotnet build --no-restore` | Fail pipeline |
| Unit tests + coverage | `dotnet test --collect:"XPlat Code Coverage"` | Fail if < 80% coverage |
| Tenant isolation tests | `dotnet test --filter Category=TenantIsolation` | Fail pipeline — no exceptions |
| Static analysis | `dotnet format --verify-no-changes` | Fail pipeline |
| Bicep lint (service infra) | `az bicep build` on any `.bicep` in service folder | Fail pipeline |
| Docker image build | `docker build` — image tagged with `{service}:{sha}` | Fail pipeline |
| Push to Azure Container Registry | `az acr push` | Fail pipeline |

Coverage report is published as a pipeline artifact and as a PR comment (via `reportgenerator` + GitHub Actions bot).

**The tenant isolation test step is a hard gate.** There is no mechanism to bypass it. A PR that fails tenant isolation tests cannot be merged.

### Frontend CI Pipeline (ci-frontend.yml)

```yaml
on:
  push:
    paths:
      - 'src/frontend/**'
```

**Steps:**

| Step | Tool | Fail behaviour |
|------|------|---------------|
| Install dependencies | `pnpm install --frozen-lockfile` | Fail pipeline |
| Type check | `pnpm turbo typecheck` | Fail pipeline |
| Lint | `pnpm turbo lint` | Fail pipeline |
| Unit tests + coverage | `pnpm turbo test` (Vitest) | Fail if < 80% coverage |
| Build affected | `pnpm turbo build --filter=[HEAD^1]` | Fail pipeline |
| E2E tests (staging only) | Playwright — runs on staging deploy, not on CI build | Fail deployment gate |

Turborepo's `--filter=[HEAD^1]` flag builds only packages and applications affected by the diff since the last commit. On a branch, this is relative to the branch base.

### Infrastructure CI Pipeline (ci-infra.yml)

```yaml
on:
  push:
    paths:
      - 'infra/**'
```

**Steps:**

| Step | Tool | Fail behaviour |
|------|------|---------------|
| Bicep build (lint) | `az bicep build` on all `.bicep` files | Fail pipeline |
| PSRule analysis | `Invoke-PSRule -Module PSRule.Rules.Azure` | Fail on Critical/High findings |
| What-if against dev | `az deployment sub what-if` | Report only — does not fail |
| What-if against test | `az deployment sub what-if` | Report only |

Actual infrastructure deployment only occurs in the CD pipeline, not CI. What-if gives visibility into planned changes without applying them.

### CD Pipeline — SaaS Platform (cd-platform.yml)

Triggered by a merge to `main`. Deploys through environments sequentially with gates.

```
merge to main
  │
  ▼
[dev] ──── automatic deploy ────────────────────────────────────────────
  │         • az containerapp update (rolling)
  │         • Bicep deploy (idempotent what-if then apply)
  │         • Smoke tests (health endpoints: /health/live, /health/ready)
  │
  ▼ (automatic — dev must pass smoke tests)
[test] ─── automatic deploy ────────────────────────────────────────────
  │         • Same as dev
  │         • Integration tests run against real Azure SQL (dev subscription)
  │         • Full tenant isolation test suite
  │         • Performance baseline check (p95 < 500ms for standard endpoints)
  │
  ▼ (manual gate — Tech Director approval required)
[staging] ─ approved deploy ────────────────────────────────────────────
  │         • Same as test
  │         • Playwright E2E tests against staff and customer portals
  │         • Data migration dry-run (if any EF Core migrations in release)
  │         • 24-hour soak period before prod gate opens
  │
  ▼ (manual gate — Tech Director approval required, after 24h soak)
[prod] ──── approved deploy ─────────────────────────────────────────────
            • Rolling deploy (Container Apps — max 1 old instance replaced at a time)
            • Automatic rollback if /health/ready fails within 5 minutes post-deploy
            • Deployment record written to dbo.Deployments (audit trail)
```

**Rollback:** Container Apps supports revision-based rollback. If `/health/ready` does not return 200 within 5 minutes of a new revision becoming active, the pipeline automatically reactivates the previous revision and raises an alert. This is configured in the Container Apps traffic weight rules, not in the pipeline YAML.

**Blue/green (opt-in):** For releases flagged as high-risk (e.g. database schema changes, auth flow changes), the CD pipeline supports a blue/green mode. A new revision receives 0% traffic. After manual validation, traffic is shifted 100% to the new revision. The old revision is retained for 1 hour before deactivation. This is triggered by adding a `blue-green: true` label to the GitHub release.

### CD Pipeline — Client-Hosted (cd-client-hosted.yml)

Client-hosted deployments are **not** automatically pushed to client subscriptions. The platform does not have standing access to client Azure subscriptions.

The CD pipeline for client-hosted:

1. Packages the `infra/client-hosted/` Bicep templates with the current version tag
2. Publishes the package to an Azure Blob Storage container (platform-managed, version-indexed)
3. Updates a `CHANGELOG.md` in the package with breaking change flags and migration notes
4. Notifies the platform operations team (via `crm.platform` topic) that a new client-hosted package is available

**Client-hosted update delivery:**

- Clients are notified of new versions via the platform admin portal and email
- Updates are **pull** — clients (or the platform team on their behalf) trigger the update by running the packaged deployment script
- Breaking infrastructure changes are flagged in the CHANGELOG and require a coordinated deployment window
- A minimum 90-day support window is maintained for each client-hosted version

### Secret Management

**No Azure credentials are stored in GitHub Secrets.** Pipeline authentication to Azure uses **Workload Identity Federation** (OIDC):

```yaml
- uses: azure/login@v2
  with:
    client-id: ${{ vars.AZURE_CLIENT_ID }}       # Non-secret — app registration client ID
    tenant-id: ${{ vars.AZURE_TENANT_ID }}        # Non-secret — directory tenant ID
    subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}  # Non-secret
```

The GitHub Actions runner authenticates via OIDC token exchange. No client secrets or certificates are stored anywhere in GitHub. The `AZURE_CLIENT_ID` and related values are **GitHub Variables** (not Secrets) — they are non-sensitive identifiers.

Runtime secrets (database connection strings, API keys) are in Azure Key Vault and accessed by services via Managed Identity. The CD pipeline does not handle these — they are configured in the Container Apps environment via Key Vault references in Bicep.

**Pipeline secrets that do exist** (GitHub Secrets, used sparingly):

| Secret | Used for | Rotation |
|--------|---------|---------|
| `COVERAGE_COMMENT_TOKEN` | GitHub bot PR comments | Annual |
| `SLACK_WEBHOOK_URL` | Pipeline failure alerts | On rotation |

No Azure resource credentials are ever in GitHub Secrets.

### Quality Gates Summary by Environment

| Gate | dev | test | staging | prod |
|------|-----|------|---------|------|
| Build passes | ✅ | ✅ | ✅ | ✅ |
| Unit tests (80% coverage) | ✅ | ✅ | ✅ | ✅ |
| Tenant isolation tests | ✅ | ✅ | ✅ | ✅ |
| Integration tests (real SQL) | ❌ | ✅ | ✅ | — |
| Smoke tests (health endpoints) | ✅ | ✅ | ✅ | ✅ |
| E2E tests (Playwright) | ❌ | ❌ | ✅ | — |
| Performance baseline | ❌ | ✅ | ✅ | — |
| Manual approval gate | ❌ | ❌ | ✅ | ✅ |
| 24h soak period | ❌ | ❌ | ✅ | — |
| Automatic rollback on health fail | ❌ | ❌ | ✅ | ✅ |

### Branch Strategy

| Branch | Purpose | Deploy target |
|--------|---------|--------------|
| `main` | Production-ready code | → dev → test → staging → prod (via CD gates) |
| `feature/*` | Feature development by agents | PR only — CI runs, no deploy |
| `hotfix/*` | Production hotfixes | → staging → prod (skips test if approved by Tech Director) |
| `release/*` | Release candidate stabilisation | → staging only |

Agents work on `feature/*` branches. All code reaches `main` via pull request. Direct pushes to `main` are blocked by branch protection rules.

Branch protection on `main`:
- Require PR with at least 1 human reviewer approval
- Require all CI status checks to pass
- Require linear history (rebase merge only — no merge commits)
- Dismiss stale reviews on new commits

---

## Consequences

### Positive
- Path-filtered per-service workflows mean a CS&S change doesn't rebuild SFA — fast CI
- Turborepo affected detection keeps frontend CI fast as the codebase grows
- Workload Identity Federation eliminates the rotating-secret problem for Azure auth
- Sequential environment gates prevent broken code reaching production
- Automatic rollback on health failure reduces MTTR for bad deployments
- Client-hosted pull model respects client sovereignty over their infrastructure

### Negative
- One workflow file per backend service — 10+ files to maintain. Template approach mitigates this but adds initial setup work.
- Manual gates at staging and prod add latency to the deployment pipeline — intentional, but requires human availability
- Client-hosted update delivery is manual — clients may run outdated versions. Version support policy (90 days) mitigates but does not eliminate this.
- Playwright E2E tests at staging add time to the staging gate — must be kept fast (< 10 minutes target)
