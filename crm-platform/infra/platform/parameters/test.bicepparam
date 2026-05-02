using '../main.bicep'

param environment = 'test'
param location = 'uksouth'
param resourceGroupName = 'crm-test-rg'
param imageTag = 'latest'

// ─── Test: same SKUs as dev, separate RG for integration test isolation ───────
param publisherEmail                = 'platform@crm-platform.dev'
param publisherName                 = 'CRM Platform Test'
param entraTenantId                 = 'TODO-entra-tenant-id'
param entraAudience                 = 'TODO-entra-app-client-id'
param deploymentPrincipalObjectId   = 'TODO-deployment-sp-object-id'
param provisionAnalyticsReplica     = false
