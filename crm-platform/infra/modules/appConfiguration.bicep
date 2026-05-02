// infra/modules/appConfiguration.bicep
// Azure App Configuration — feature flags and non-secret config values.
// ADR 0006: App Config for runtime flags; Key Vault for secrets.

@description('Azure region')
param location string

@description('App Configuration store name (5-50 chars, globally unique)')
param appConfigName string

@description('SKU — free for dev, standard for prod (SLA + purge protection)')
@allowed(['free', 'standard'])
param sku string = 'standard'

@description('Object IDs of Managed Identities needing App Configuration Data Reader')
param dataReaderPrincipalIds array = []

@description('Initial key-values to seed [{key, value, label?, contentType?}]')
param initialKeyValues array = []

@description('Tags')
param tags object = {}

var appConfigDataReaderRoleId = '516239f1-63e1-4d78-a4de-a74fb236a071'

resource appConfig 'Microsoft.AppConfiguration/configurationStores@2023-03-01' = {
  name: appConfigName
  location: location
  tags: tags
  sku: { name: sku }
  properties: {
    disableLocalAuth: true
    publicNetworkAccess: 'Disabled'
    enablePurgeProtection: sku == 'standard'
    softDeleteRetentionInDays: sku == 'standard' ? 7 : 0
  }
}

resource dataReaderAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in dataReaderPrincipalIds: {
  name: guid(appConfig.id, principalId, appConfigDataReaderRoleId)
  scope: appConfig
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', appConfigDataReaderRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

resource keyValues 'Microsoft.AppConfiguration/configurationStores/keyValues@2023-03-01' = [for kv in initialKeyValues: {
  parent: appConfig
  name: empty(kv.?label ?? '') ? kv.key : '${kv.key}$${kv.label}'
  properties: {
    value: kv.value
    contentType: kv.?contentType ?? 'text/plain'
  }
}]

output appConfigId string = appConfig.id
output appConfigEndpoint string = appConfig.properties.endpoint

@description('Location for all resources')
param location string = resourceGroup().location

@description('Resource name')
param name string

// TODO: add parameters, resources, and outputs
