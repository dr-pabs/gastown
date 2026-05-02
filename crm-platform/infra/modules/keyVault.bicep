// infra/modules/keyVault.bicep
// Azure Key Vault — RBAC model (not legacy access policies).
// All secrets accessed via Managed Identity + Key Vault Secrets User role.
// ADR 0004 / ADR 0006: no secrets in appsettings or env vars.

@description('Azure region')
param location string

@description('Key Vault name (3-24 chars, globally unique)')
param keyVaultName string

@description('Object IDs of Managed Identities that need Key Vault Secrets User')
param secretsUserPrincipalIds array = []

@description('Object IDs of identities that need Key Vault Secrets Officer (CI/CD)')
param secretsOfficerPrincipalIds array = []

@description('Tags')
param tags object = {}

var kvSecretsUserRoleId    = '4633458b-17de-408a-b874-0445c86b69e6'
var kvSecretsOfficerRoleId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Disabled'
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
  }
}

resource secretsUserAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in secretsUserPrincipalIds: {
  name: guid(keyVault.id, principalId, kvSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

resource secretsOfficerAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in secretsOfficerPrincipalIds: {
  name: guid(keyVault.id, principalId, kvSecretsOfficerRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsOfficerRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'kv-diagnostics'
  scope: keyVault
  properties: {
    logs: [{ category: 'AuditEvent', enabled: true, retentionPolicy: { enabled: true, days: 90 } }]
    metrics: [{ category: 'AllMetrics', enabled: true }]
  }
}

output keyVaultId string = keyVault.id
output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultName string = keyVault.name

@description('Location for all resources')
param location string = resourceGroup().location

@description('Resource name')
param name string

// TODO: add parameters, resources, and outputs
