# Runbook: Provision Client-Hosted Deployment

**Severity:** N/A — planned operation  
**References:** [ADR 0009 — Tenant Provisioning and Lifecycle](../adr/0009-tenant-provisioning-and-lifecycle.md)  
**Owner:** Platform Engineering + Account Team  
**Last reviewed:** 2026-04-12

---

## Overview

A client-hosted deployment provisions a **dedicated Azure subscription** for a single enterprise customer. The entire CRM platform runs in the customer's own subscription (or a Microsoft-managed subscription on their behalf), isolated from all other tenants.

This runbook covers the full end-to-end provisioning procedure including pre-flight checks, infrastructure deployment, service configuration, and handover verification.

---

## When to Use This Runbook

Use this runbook when:
- A new enterprise customer has contracted for a client-hosted deployment
- An existing SaaS tenant is migrating to client-hosted
- A client-hosted environment needs to be rebuilt (disaster recovery scenario)

Do **not** use this runbook for:
- Standard SaaS tenant provisioning (use `scripts/provision-tenant.sh` instead)
- Adding a new tenant to an existing SaaS deployment

---

## Prerequisites

### Account team must provide

- [ ] Customer Azure subscription ID (or confirmation that Microsoft will create one)
- [ ] Customer Azure tenant ID (Entra ID)
- [ ] Customer's preferred Azure region
- [ ] Agreed service tier (`starter` / `growth` / `professional`)
- [ ] Customer admin email address
- [ ] Signed contract with data residency requirements noted
- [ ] Customer IT contact for Entra ID app registration approval

### Engineering prerequisites

- [ ] `az login` with Owner rights on the target subscription
- [ ] Bicep CLI installed: `az bicep install`
- [ ] Access to `crm-prod-kv` to copy production-baseline secrets to client Key Vault
- [ ] Azure DevOps or GitHub Actions service principal created for the client's CD pipeline

---

## 1. Pre-Flight Checks

### 1.1 Verify subscription quotas

```bash
# Check Container App Environment quota in target region
az containerapp env list \
  --subscription <client-subscription-id> \
  --query "[].{name:name, region:location}" -o table

# Check Azure SQL vCore quota
az sql mi list \
  --subscription <client-subscription-id> \
  -o table
```

Ensure the target region has available quota for:
- Container Apps Environment (1)
- Azure SQL (Standard S2 minimum, or as per contract)
- Azure Service Bus Premium (1 namespace)
- Azure Key Vault (1)
- Azure Container Registry (1)
- Azure Static Web Apps (1 — staff portal)

### 1.2 Verify Entra ID access

Confirm that someone with Global Administrator or Application Administrator role in the customer's Entra tenant is available to approve the app registrations created during provisioning.

### 1.3 Review data residency requirements

Check the signed contract for data residency constraints. Ensure the target region is compliant. If the contract specifies EU data residency, do not deploy to a non-EU region even if quota is available elsewhere.

---

## 2. Infrastructure Deployment

### 2.1 Prepare parameters

Copy the client-hosted parameter template:

```bash
cp infra/client-hosted/parameters/client-template.bicepparam \
   infra/client-hosted/parameters/<client-name>.bicepparam
```

Edit `<client-name>.bicepparam` with the client-specific values:

```bicep
param environment = 'prod'
param clientName  = '<client-short-name>'       // e.g. 'acme' — used in resource names
param location    = '<azure-region>'            // e.g. 'uksouth'
param sqlTier     = 'Standard'                  // or 'Premium' per contract
param adminEmail  = '<client-admin@client.com>'
```

Commit this file to the repository in a branch named `client-hosted/<client-name>`.

### 2.2 Deploy infrastructure

```bash
az deployment sub create \
  --subscription <client-subscription-id> \
  --location <azure-region> \
  --template-file infra/platform/main.bicep \
  --parameters infra/client-hosted/parameters/<client-name>.bicepparam \
  --name "crm-client-<client-name>-$(date +%Y%m%d)"
```

Expected deployment time: **20–35 minutes**.

Capture the deployment outputs:

```bash
az deployment sub show \
  --subscription <client-subscription-id> \
  --name "crm-client-<client-name>-<date>" \
  --query properties.outputs \
  -o json > /tmp/client-<client-name>-outputs.json
```

### 2.3 Verify deployed resources

```bash
az resource list \
  --subscription <client-subscription-id> \
  --resource-group crm-prod-<client-name>-rg \
  --output table
```

Expected resources:
- Container Apps Environment
- 9 × Container Apps (one per service)
- Azure SQL Server + Database
- Azure Service Bus Namespace (Premium)
- Azure Key Vault
- Azure Container Registry
- Azure App Configuration
- Azure Storage Account
- Application Insights + Log Analytics Workspace
- 1 × Azure Static Web App (staff portal)
- APIM instance (if contracted)

---

## 3. Post-Deployment Configuration

### 3.1 Seed Key Vault secrets

The Bicep deployment creates the Key Vault but does not populate secrets. Run:

```bash
./scripts/provision-client-keyvault.sh \
  --subscription <client-subscription-id> \
  --resource-group crm-prod-<client-name>-rg \
  --client-name <client-name>
```

This script prompts for each secret value and stores them securely. Secrets required:

| Secret Name | Description |
| --- | --- |
| `AiFoundry--Endpoint` | Client's Azure AI Foundry endpoint |
| `AiFoundry--ApiKey` | Client's Azure AI Foundry API key |
| `Entra--StaffClientId` | App registration client ID (staff portal) |
| `Acs--ConnectionString` | Azure Communication Services connection string |

### 3.2 Configure Entra ID app registrations

One app registration is required in the **customer's Entra tenant**:

**Staff portal (corporate Entra ID):**
1. Create app registration: `CRM Staff Portal - <ClientName>`
2. Add redirect URI: `https://<staff-portal-url>/auth/callback`
3. Add API permissions: `User.Read`, `offline_access`
4. Record the Client ID and store in Key Vault as `Entra--StaffClientId`

The customer's IT admin must approve consent for the staff portal app registration.

### 3.3 Apply database migrations

```bash
# Run EF Core migrations against the client's SQL instance
./scripts/apply-migrations-client.sh \
  --subscription <client-subscription-id> \
  --resource-group crm-prod-<client-name>-rg
```

### 3.4 Provision the initial tenant

```bash
./scripts/provision-tenant.sh \
  --tenant-id <new-uuid> \
  --name "<Client Company Name>" \
  --tier <starter|growth|professional> \
  --admin-email "<client-admin@client.com>"
```

---

## 4. CD Pipeline Setup

### 4.1 Create service principal

```bash
az ad sp create-for-rbac \
  --name "crm-cicd-<client-name>" \
  --role Contributor \
  --scopes /subscriptions/<client-subscription-id>/resourceGroups/crm-prod-<client-name>-rg \
  --sdk-auth
```

Store the output JSON as the `AZURE_CREDENTIALS` secret in the client's GitHub repository (or Azure DevOps).

### 4.2 Configure the CD workflow

The `cd-client-hosted.yml` workflow uses the following environment variables. Set these as repository secrets:

| Secret | Value |
| --- | --- |
| `AZURE_CREDENTIALS` | Service principal JSON from step 4.1 |
| `AZURE_SUBSCRIPTION_ID` | Client subscription ID |
| `AZURE_RESOURCE_GROUP` | `crm-prod-<client-name>-rg` |
| `ACR_LOGIN_SERVER` | Container registry login server URL |

---

## 5. Handover Verification Checklist

Before signing off the deployment as complete, verify each item:

### Infrastructure
- [ ] All 9 Container Apps are in `Running` state
- [ ] SQL database is accessible and migrations are applied
- [ ] Service Bus topics and subscriptions are created
- [ ] Key Vault secrets are all populated (no empty values)
- [ ] Application Insights is receiving telemetry (check Live Metrics)

### Authentication
- [ ] Staff portal loads and Entra ID login completes successfully
- [ ] Customer portal loads and Entra External ID login completes successfully
- [ ] A test user in the client's Entra tenant can sign in to the staff portal
- [ ] JWT claims include correct `TenantId` and role claims

### Functional smoke tests
- [ ] Create a lead via the staff portal → appears in SFA module
- [ ] Create a support case via the customer portal → appears in CS&S module
- [ ] Service Bus event is published and consumed (check Application Insights traces)
- [ ] An outbound email (notification) is delivered to a test inbox

### Security
- [ ] `/health/live` and `/health/ready` endpoints return 200 on all services
- [ ] Unauthenticated request to any API endpoint returns 401
- [ ] TenantB JWT cannot access TenantA data (run tenant isolation smoke test)

---

## 6. Post-Provisioning

1. Email the client admin with:
   - Portal URLs (staff + customer)
   - Admin account credentials / invite link
   - Link to the customer's dedicated runbook repo (if created)
2. Schedule a 30-minute handover call to walk the admin through the portals
3. Notify the Account team the deployment is live
4. Create a row in the internal client registry spreadsheet with all resource IDs and URLs
5. Set a calendar reminder to review the deployment after 30 days (stability check)

---

## Rollback

If the deployment fails and must be rolled back:

```bash
# Delete the resource group (removes all deployed resources)
az group delete \
  --subscription <client-subscription-id> \
  --name crm-prod-<client-name>-rg \
  --yes --no-wait
```

> This is irreversible for any data that was written. Only perform a full rollback if no real customer data has been written and the deployment is being retried from scratch.

---

## Related

- [ADR 0009 — Tenant Provisioning and Lifecycle](../adr/0009-tenant-provisioning-and-lifecycle.md)
- [ADR 0003 — IaC: Bicep over Terraform](../adr/0003-iac-bicep-over-terraform.md)
- `infra/client-hosted/` — Bicep templates for client-hosted deployments
- `scripts/provision-tenant.sh` — SaaS tenant provisioning script
- `cd-client-hosted.yml` — CD pipeline for client-hosted deployments
