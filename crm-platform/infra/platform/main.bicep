// CRM Platform — Main Bicep entry point
// Orchestrates all platform modules for a given environment.
// Deploy via: az deployment sub create --location uksouth --template-file main.bicep --parameters @parameters/prod.bicepparam

targetScope = 'subscription'

@description('Environment name (dev | test | staging | prod)')
@allowed(['dev', 'test', 'staging', 'prod'])
param environment string

@description('Azure region for all resources')
param location string = 'uksouth'

@description('Resource group name')
param resourceGroupName string = 'crm-${environment}-rg'

@description('Container image tag to deploy')
param imageTag string

@description('Publisher email for APIM')
param publisherEmail string

@description('Publisher organisation name for APIM')
param publisherName string = 'CRM Platform'

@description('Entra ID tenant ID for JWT validation')
param entraTenantId string

@description('Entra ID audience (App Registration client ID)')
param entraAudience string

@description('Object ID of the deployment principal (for SQL AAD admin)')
param deploymentPrincipalObjectId string

@description('Whether to provision an analytics named replica')
param provisionAnalyticsReplica bool = environment == 'prod'

// ─── Resource name helpers ────────────────────────────────────────────────────
var prefix = 'crm-${environment}'
var sqlServerName        = '${prefix}-sql'
var sbNamespaceName      = '${prefix}-sb'
var keyVaultName         = '${prefix}-kv'
var storageAccountName   = replace('${prefix}sa', '-', '')
var appConfigName        = '${prefix}-appconfig'
var apimName             = '${prefix}-apim'
var staffPortalName      = '${prefix}-staff-portal'

// ─── SKU map per environment ──────────────────────────────────────────────────
var sqlSkuTier = environment == 'prod' ? 'Hyperscale' : 'GeneralPurpose'
var sqlVCores  = environment == 'prod' ? 4 : 2
var sbCapacity = environment == 'prod' ? 2 : 1
var storageSku = environment == 'prod' ? 'Standard_ZRS' : 'Standard_LRS'
var apimSku    = environment == 'prod' ? 'Premium' : 'Developer'
var swaSku     = environment == 'prod' ? 'Standard' : 'Free'
var appConfigSku = environment == 'prod' ? 'standard' : 'free'

// ─── Resource Group ───────────────────────────────────────────────────────────
resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' = {
  name: resourceGroupName
  location: location
  tags: {
    environment: environment
    project: 'crm-platform'
    managedBy: 'bicep'
  }
}

// ─── Key Vault ────────────────────────────────────────────────────────────────
module keyVault '../modules/keyVault.bicep' = {
  name: 'keyVault'
  scope: rg
  params: {
    location: location
    keyVaultName: keyVaultName
    tags: rg.tags
    // Services granted Secrets User after Container Apps are deployed (separate pass)
    secretsUserPrincipalIds: []
  }
}

// ─── Storage Account ──────────────────────────────────────────────────────────
module storage '../modules/storageAccount.bicep' = {
  name: 'storage'
  scope: rg
  params: {
    location: location
    storageAccountName: storageAccountName
    sku: storageSku
    blobContainers: ['attachments', 'durable-functions']
    tags: rg.tags
  }
}

// ─── Service Bus ──────────────────────────────────────────────────────────────
module serviceBus '../modules/serviceBus.bicep' = {
  name: 'serviceBus'
  scope: rg
  params: {
    location: location
    namespaceName: sbNamespaceName
    capacity: sbCapacity
    tags: rg.tags
  }
}

// ─── SQL Database ─────────────────────────────────────────────────────────────
module sql '../modules/sqlDatabase.bicep' = {
  name: 'sqlDatabase'
  scope: rg
  params: {
    location: location
    serverName: sqlServerName
    databaseName: 'CrmPlatform'
    skuTier: sqlSkuTier
    vCores: sqlVCores
    provisionAnalyticsReplica: provisionAnalyticsReplica
    sqlAdminObjectId: deploymentPrincipalObjectId
    sqlAdminDisplayName: 'crm-platform-sql-admin'
    tags: rg.tags
  }
}

// ─── App Configuration ────────────────────────────────────────────────────────
module appConfig '../modules/appConfiguration.bicep' = {
  name: 'appConfiguration'
  scope: rg
  params: {
    location: location
    appConfigName: appConfigName
    sku: appConfigSku
    tags: rg.tags
    initialKeyValues: [
      { key: 'CRM:Environment',    value: environment }
      { key: 'CRM:ImageTag',       value: imageTag    }
    ]
  }
}

// ─── API Management ───────────────────────────────────────────────────────────
module apim '../modules/apiManagement.bicep' = {
  name: 'apiManagement'
  scope: rg
  params: {
    location: location
    apimName: apimName
    publisherEmail: publisherEmail
    publisherName: publisherName
    sku: apimSku
    skuCapacity: 1
    entraTenantId: entraTenantId
    entraAudience: entraAudience
    tags: rg.tags
  }
}

// ─── Static Web Apps ──────────────────────────────────────────────────────────
module staffPortal '../modules/staticWebApp.bicep' = {
  name: 'staffPortal'
  scope: rg
  params: {
    location: location
    staticWebAppName: staffPortalName
    sku: swaSku
    tags: rg.tags
  }
}

// ─── Outputs (referenced by CI/CD pipelines) ──────────────────────────────────
output resourceGroupName string = rg.name
output keyVaultUri string = keyVault.outputs.keyVaultUri
output sqlServerFqdn string = sql.outputs.serverFqdn
output serviceBusFqdn string = serviceBus.outputs.namespaceFqdn
output appConfigEndpoint string = appConfig.outputs.appConfigEndpoint
output apimGatewayUrl string = apim.outputs.apimGatewayUrl
output staffPortalHostname string = staffPortal.outputs.defaultHostname
