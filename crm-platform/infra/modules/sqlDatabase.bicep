// infra/modules/sqlDatabase.bicep
// Azure SQL Hyperscale — single shared database, multi-tenant via RLS.
// Named replica provisioned separately for analytics read traffic.
// ADR 0003: Azure SQL Hyperscale chosen over Cosmos DB.

@description('Azure region for all resources')
param location string

@description('Name of the SQL server resource')
param serverName string

@description('Name of the database')
param databaseName string = 'CrmPlatform'

@description('SKU tier — use GeneralPurpose for dev, Hyperscale for prod')
@allowed(['GeneralPurpose', 'Hyperscale'])
param skuTier string = 'Hyperscale'

@description('Number of vCores for the database')
param vCores int = 2

@description('Whether to provision a named replica for analytics (read-only)')
param provisionAnalyticsReplica bool = false

@description('Object ID of the Managed Identity to assign as Azure AD admin')
param sqlAdminObjectId string

@description('Display name of the AAD admin')
param sqlAdminDisplayName string

@description('Tags to apply to all resources')
param tags object = {}

// ─── SQL Server ───────────────────────────────────────────────────────────────
resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminDisplayName
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
  }
}

// ─── Primary database ─────────────────────────────────────────────────────────
resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: skuTier == 'Hyperscale' ? 'Hyperscale' : 'GP_Gen5'
    tier: skuTier
    capacity: vCores
    family: 'Gen5'
  }
  properties: {
    requestedBackupStorageRedundancy: 'Zone'
    highAvailabilityReplicaCount: skuTier == 'Hyperscale' ? 1 : 0
    readScale: 'Disabled'
  }
}

// ─── Named replica for analytics (ADR 0003) ──────────────────────────────────
resource analyticsReplica 'Microsoft.Sql/servers/databases@2023-05-01-preview' = if (provisionAnalyticsReplica) {
  parent: sqlServer
  name: '${databaseName}-analytics'
  location: location
  tags: tags
  sku: {
    name: 'Hyperscale'
    tier: 'Hyperscale'
    capacity: 2
    family: 'Gen5'
  }
  properties: {
    createMode: 'Secondary'
    sourceDatabaseId: sqlDatabase.id
    secondaryType: 'Named'
  }
}

// ─── Auditing ─────────────────────────────────────────────────────────────────
resource serverAudit 'Microsoft.Sql/servers/auditingSettings@2023-05-01-preview' = {
  parent: sqlServer
  name: 'default'
  properties: {
    state: 'Enabled'
    isAzureMonitorTargetEnabled: true
    retentionDays: 90
  }
}

// ─── Advanced Threat Protection ───────────────────────────────────────────────
resource threatProtection 'Microsoft.Sql/servers/advancedThreatProtectionSettings@2023-05-01-preview' = {
  parent: sqlServer
  name: 'Default'
  properties: {
    state: 'Enabled'
  }
}

// ─── Outputs ─────────────────────────────────────────────────────────────────
output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName
output serverId string = sqlServer.id
output databaseId string = sqlDatabase.id
output analyticsReplicaId string = provisionAnalyticsReplica ? analyticsReplica.id : ''

@description('Location for all resources')
param location string = resourceGroup().location

@description('Resource name')
param name string

// TODO: add parameters, resources, and outputs
