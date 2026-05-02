# ADR 0006 — Frontend Architecture

**Status:** Accepted  
**Date:** 2026-04-04  
**Deciders:** Technical Director  
**Supersedes:** —  
**Superseded by:** —

---

## Context

The platform has two frontend applications:

- **Staff portal** (`src/frontend/staff-portal`) — the primary CRM UI used by sales reps, support agents, managers, and tenant admins. Complex, multi-module, high data density.
- **Customer portal** (`src/frontend/customer-portal`) — a customer-facing interface for viewing cases, submitting requests, and managing account details. Simpler, narrower scope.

Both are TypeScript/React on Azure Static Web Apps. Without architectural decisions recorded here, agents will make inconsistent choices on state management, authentication, API communication, component structure, and monorepo layout — producing two portals that are difficult to maintain together.

The following must be resolved:

1. Monorepo structure and shared package strategy
2. Authentication library and token acquisition
3. API communication layer — how requests are made, headers injected, errors handled
4. State management approach
5. Component library / design system
6. Build, lint, and type-checking standards

---

## Options Considered

### State Management
- **Redux Toolkit** — powerful, verbose, best for very large apps with complex cross-cutting state
- **Zustand** — lightweight, minimal boilerplate, composable stores
- **React Query (TanStack Query) + Zustand** — React Query owns server state (fetching, caching, invalidation); Zustand owns client-only UI state. Separation of concerns is clear.
- **Context API only** — insufficient for the staff portal's complexity at scale

### Authentication
- **MSAL.js (`@azure/msal-browser` + `@azure/msal-react`)** — Microsoft's official library for Entra ID and Entra External ID token acquisition. Handles token refresh, silent acquisition, and redirect flows.
- **Custom OAuth implementation** — reinventing the wheel. Rejected.

### Component Library
- **MUI (Material UI)** — comprehensive, well-supported, opinionated
- **Radix UI primitives + Tailwind CSS** — unstyled, accessible primitives; full design control via utility classes. More work upfront, better long-term flexibility for a product with its own brand.
- **shadcn/ui (Radix + Tailwind, pre-composed)** — pragmatic middle ground: accessible components, copy-paste model (no library lock-in), Tailwind-styled, easy to customise.

---

## Decision

**Adopt a monorepo with shared packages. Use MSAL React for authentication, TanStack Query for server state, Zustand for client state, and shadcn/ui (Radix + Tailwind) as the component foundation.**

---

## Detail

### Monorepo Structure

```
src/frontend/
├── staff-portal/          → Staff CRM application
├── customer-portal/       → Customer-facing application
└── packages/
    ├── ui/                → Shared component library (shadcn/ui base + platform overrides)
    ├── auth/              → Shared MSAL configuration and hooks
    ├── api-client/        → Generated API client + Axios instance with interceptors
    ├── types/             → Shared TypeScript types (mirroring backend DTOs)
    └── utils/             → Shared utilities (date formatting, validation, etc.)
```

Each `packages/*` entry is a TypeScript package referenced via workspace paths. Both portals consume from `packages/` — they never copy code between them.

Tooling:
- **pnpm workspaces** for package management
- **Turborepo** for build orchestration (incremental builds — only rebuild what changed)
- **TypeScript project references** for cross-package type checking

### Authentication

**Library:** `@azure/msal-browser` + `@azure/msal-react`

Both portals use MSAL, but with different authority configurations:

| Portal | Authority | Client ID |
|--------|-----------|-----------|
| Staff portal | `https://login.microsoftonline.com/{platform-tenant-id}` | Staff app registration client ID |
| Customer portal | `https://{b2c-tenant}.ciamlogin.com/{b2c-tenant}.onmicrosoft.com` | Customer app registration client ID |

MSAL configuration lives in `packages/auth/` and is parameterised — portals pass their environment-specific values at initialisation. This ensures the auth flow is not duplicated.

**Token acquisition pattern:**

```
Component renders
  → useMsal() hook checks token cache
  → Silent acquisition if token present and valid
  → Redirect/popup acquisition if token absent or expired
  → Token stored in MSAL cache (sessionStorage, not localStorage)
  → Every API call uses token from cache via acquireTokenSilent()
```

Token storage: **sessionStorage** (not localStorage) — tokens do not persist across browser sessions, reducing XSS risk.

**No token is ever manually stored outside MSAL's cache.** The `packages/api-client/` Axios interceptor calls `acquireTokenSilent()` on every request and injects the `Authorization: Bearer` header. No component or hook manually handles tokens.

### API Communication

**Library:** Axios, configured in `packages/api-client/`

A single Axios instance is created per portal with:

| Interceptor | Purpose |
|-------------|---------|
| Request — auth | Calls `acquireTokenSilent()`, injects `Authorization: Bearer {token}` |
| Request — tenant | Injects `X-Tenant-Id` header (resolved from MSAL account `tenantId`) |
| Request — correlation | Injects `X-Correlation-Id` (UUID generated per request) |
| Response — error | Maps HTTP error responses to typed `ApiError` objects |
| Response — 401 | Triggers MSAL re-authentication flow |
| Response — 403 | Dispatches to global "insufficient permissions" UI state |

API client methods are **generated** from the OpenAPI spec exported by each backend service. The generation step runs in CI. Agents must not hand-write API calls against backend endpoints — they use the generated client.

Generation tooling: **openapi-typescript-codegen** (or equivalent configured in `packages/api-client/package.json`).

### Server State Management — TanStack Query

All data fetched from the API is managed by **TanStack Query** (`@tanstack/react-query`).

- Queries (GET operations) are defined as `useQuery` hooks, co-located with the component or feature that owns them
- Mutations (POST/PUT/PATCH/DELETE) are defined as `useMutation` hooks
- Cache invalidation is explicit — after a mutation, the relevant query keys are invalidated
- Stale time defaults: **30 seconds** for list queries, **60 seconds** for detail queries (configurable per query)
- No manual `useEffect` + `fetch`/`axios` for data fetching. All data fetching goes through TanStack Query. No exceptions.

### Client State Management — Zustand

UI-only state (sidebar open/closed, active module tab, notification queue, modal state) is managed by **Zustand**.

- One store per domain concern (e.g. `useLayoutStore`, `useNotificationStore`)
- Stores are defined in `staff-portal/src/stores/` and `customer-portal/src/stores/` — not in `packages/` (client state is portal-specific)
- Stores are not persisted to localStorage by default — explicit opt-in required with justification

### Component Library

**shadcn/ui** (Radix UI primitives + Tailwind CSS) is the component foundation.

- Components are **copied into** `packages/ui/src/components/` (shadcn's copy model) — not installed as a dependency. This gives full control over component internals.
- Tailwind configuration is shared via `packages/ui/tailwind.config.ts` and extended per portal
- The platform design token set (colours, spacing, typography) is defined in `packages/ui/src/tokens/` as CSS custom properties and Tailwind config values
- Agents must not install additional component libraries (e.g. MUI, Ant Design) without Tech Director approval
- Accessibility: all interactive components must meet WCAG 2.1 AA. Radix UI primitives handle keyboard navigation and ARIA by default — agents must not override ARIA attributes without justification.

### Code Quality Standards

| Tool | Config location | Enforced in CI |
|------|----------------|----------------|
| TypeScript | `tsconfig.json` per package, `strict: true` | Yes — `tsc --noEmit` fails build |
| ESLint | `.eslintrc` at root, extended per package | Yes |
| Prettier | `.prettierrc` at root | Yes — format check in CI |
| Vitest | Per portal `vitest.config.ts` | Yes — 80% coverage gate (mirrors backend) |
| Playwright | `playwright.config.ts` at root | Yes — E2E on staging before prod deploy |

Specific ESLint rules enforced:
- `no-console` — use structured logging utility from `packages/utils/` instead
- `react-hooks/exhaustive-deps` — no suppression without justification comment
- `@typescript-eslint/no-explicit-any` — `any` is banned; use `unknown` and narrow

### Environment Configuration

All environment-specific values (APIM base URL, MSAL client IDs, tenant IDs) are injected at build time via environment variables. **No secrets in frontend code or environment files committed to the repository.**

Azure Static Web Apps handles environment configuration via app settings in the Bicep deployment. Local development uses `.env.local` files (gitignored).

---

## Consequences

### Positive
- Monorepo with shared packages eliminates code duplication between portals
- MSAL handles the complexity of token acquisition and refresh transparently
- TanStack Query + Zustand gives a clear server/client state boundary — easier to reason about
- Generated API client ensures frontend stays in sync with backend contract
- shadcn/ui gives accessible, customisable components without framework lock-in
- Turborepo incremental builds keep CI fast as the codebase grows

### Negative
- Turborepo and pnpm workspaces add initial setup complexity
- Generated API client requires OpenAPI specs to be published and up to date — backend teams must treat their OpenAPI spec as a first-class output
- shadcn copy model means component updates from upstream must be manually reviewed and applied
- Two MSAL authority configurations must be kept in sync with the identity infrastructure (ADR 0004)
