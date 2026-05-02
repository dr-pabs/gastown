using '../main.bicep'

param environment = 'dev'
param location = 'uksouth'
param resourceGroupName = 'crm-dev-rg'
param imageTag = 'latest'

// ─── Dev: cheap SKUs, no HA, no private endpoints ────────────────────────────
// SQL: GeneralPurpose 2 vCores | SB: 1 MU | Storage: LRS | APIM: Developer | SWA: Free
param publisherEmail                = 'platform@crm-platform.dev'
param publisherName                 = 'CRM Platform Dev'
param entraTenantId                 = 'TODO-entra-tenant-id'
param entraAudience                 = 'TODO-entra-app-client-id'
param deploymentPrincipalObjectId   = 'TODO-deployment-sp-object-id'
param provisionAnalyticsReplica     = false
