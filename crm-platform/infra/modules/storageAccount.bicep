// infra/modules/storageAccount.bicep
// Azure Storage Account — Durable Functions state, blob attachments.
// Managed Identity only — no shared keys.

@description('Azure region')
param location string

@description('Storage account name (3-24 chars, lowercase, globally unique)')
param storageAccountName string

@description('SKU — Standard_LRS for dev, Standard_ZRS for prod')
@allowed(['Standard_LRS', 'Standard_ZRS', 'Standard_GRS', 'Standard_RAGRS'])
param sku string = 'Standard_ZRS'

@description('Array of blob container names to create')
param blobContainers array = []

@description('Object IDs needing Storage Blob Data Contributor')
param blobContributorPrincipalIds array = []

@description('Tags')
param tags object = {}

var blobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: sku }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 7 }
    containerDeleteRetentionPolicy: { enabled: true, days: 7 }
  }
}

resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = [for name in blobContainers: {
  parent: blobService
  name: name
  properties: { publicAccess: 'None' }
}]

resource blobContributorAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in blobContributorPrincipalIds: {
  name: guid(storageAccount.id, principalId, blobDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributorRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

output storageAccountId string = storageAccount.id
output storageAccountName string = storageAccount.name
output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob

@description('Location for all resources')
param location string = resourceGroup().location

@description('Resource name')
param name string

// TODO: add parameters, resources, and outputs
