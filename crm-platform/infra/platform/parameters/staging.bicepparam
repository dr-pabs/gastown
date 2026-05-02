using '../main.bicep'

param environment = 'staging'
param location = 'uksouth'
param resourceGroupName = 'crm-staging-rg'
param imageTag = 'latest'

// ─── Staging: prod-equivalent SKUs for performance validation ────────────────
// SQL: Hyperscale 2 vCores | SB: 1 MU | Storage: ZRS | APIM: Standard | SWA: Standard
param publisherEmail                = 'platform@crm-platform.dev'
param publisherName                 = 'CRM Platform Staging'
param entraTenantId                 = 'TODO-entra-tenant-id'
param entraAudience                 = 'TODO-entra-app-client-id'
param deploymentPrincipalObjectId   = 'TODO-deployment-sp-object-id'
param provisionAnalyticsReplica     = false
