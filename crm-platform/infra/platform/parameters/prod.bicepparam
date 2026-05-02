using '../main.bicep'

param environment = 'prod'
param location = 'uksouth'
param resourceGroupName = 'crm-prod-rg'
param imageTag = 'latest'

// ─── Production: full HA SKUs ─────────────────────────────────────────────────
// SQL: Hyperscale 4 vCores + named replica | SB: 2 MU | Storage: ZRS
// APIM: Premium | SWA: Standard | AppConfig: standard
param publisherEmail                = 'platform@crm-platform.io'
param publisherName                 = 'CRM Platform'
param entraTenantId                 = 'TODO-entra-tenant-id'
param entraAudience                 = 'TODO-entra-app-client-id'
param deploymentPrincipalObjectId   = 'TODO-deployment-sp-object-id'
param provisionAnalyticsReplica     = true
