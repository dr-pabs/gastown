// infra/modules/serviceBus.bicep
// Azure Service Bus Premium namespace + all 7 CRM topics + per-service subscriptions.
// ADR 0002: Service Bus is the ONLY channel for service-to-service communication.
// Premium tier required for VNet integration, sessions, and private endpoints.

@description('Azure region')
param location string

@description('Service Bus namespace name')
param namespaceName string

@description('Tags')
param tags object = {}

@description('Capacity units (1 per Premium messaging unit). Use 1 for dev, 2+ for prod.')
param capacity int = 1

// ─── Namespace ────────────────────────────────────────────────────────────────
resource sbNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: 'Premium'
    tier: 'Premium'
    capacity: capacity
  }
  properties: {
    premiumMessagingPartitions: 1
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    zoneRedundant: true
  }
}

// ─── Topics ───────────────────────────────────────────────────────────────────
var topics = [
  { name: 'crm.sfa',       maxSize: 10240 }
  { name: 'crm.css',       maxSize: 10240 }
  { name: 'crm.marketing', maxSize: 10240 }
  { name: 'crm.identity',  maxSize: 5120  }
  { name: 'crm.platform',  maxSize: 5120  }
  { name: 'crm.analytics', maxSize: 10240 }
  { name: 'crm.ai',        maxSize: 5120  }
]

resource sbTopics 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = [for topic in topics: {
  parent: sbNamespace
  name: topic.name
  properties: {
    maxSizeInMegabytes: topic.maxSize
    defaultMessageTimeToLive: 'P7D'
    enableBatchedOperations: true
    supportOrdering: false
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
  }
}]

// ─── Subscriptions ────────────────────────────────────────────────────────────
// crm.sfa
resource sfaSubCss 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: sbTopics[0]
  name: 'css-service'
  properties: { lockDuration: 'PT5M', maxDeliveryCount: 3 }
}
resource sfaSubMarketing 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: sbTopics[0]
  name: 'marketing-service'
  properties: { lockDuration: 'PT5M', maxDeliveryCount: 3 }
}
resource sfaSubAnalytics 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: sbTopics[0]
  name: 'analytics-service'
  properties: { lockDuration: 'PT5M', maxDeliveryCount: 3 }
}
resource sfaSubNotification 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: sbTopics[0]
  name: 'notification-service'
  properties: { lockDuration: 'PT5M', maxDeliveryCount: 3 }
}

// crm.css
resource cssSubNotification 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: sbTopics[1]
  name: 'notification-service'
  properties: { lockDuration: 'PT5M', maxDeliveryCount: 3 }
}
resource cssSubAnalytics 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: sbTopics[1]
  name: 'analytics-service'
  properties: { lockDuration: 'PT5M', maxDeliveryCount: 3 }
}

// crm.marketing
resource marketingSubAnalytics 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: sbTopics[2]
  name: 'analytics-service'
  properties: { lockDuration: 'PT5M', maxDeliveryCount: 3 }
}
resource marketingSubNotification 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: sbTopics[2]
  name: 'notification-service'
  properties: { lockDuration: 'PT5M', maxDeliveryCount: 3 }
}
resource marketingSubSfa 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: sbTopics[2]
  name: 'sfa-service'
  properties: { lockDuration: 'PT5M', maxDeliveryCount: 3 }
}

// crm.identity
resource identitySubAll 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: sbTopics[3]
  name: 'all-services'
  properties: { lockDuration: 'PT5M', maxDeliveryCount: 3 }
}

// crm.platform
resource platformSubAll 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: sbTopics[4]
  name: 'all-services'
  properties: { lockDuration: 'PT5M', maxDeliveryCount: 3 }
}

// crm.analytics
resource analyticsSubIntegration 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: sbTopics[5]
  name: 'integration-service'
  properties: { lockDuration: 'PT5M', maxDeliveryCount: 3 }
}

// crm.ai
resource aiSubSfa 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: sbTopics[6]
  name: 'sfa-service'
  properties: { lockDuration: 'PT5M', maxDeliveryCount: 3 }
}
resource aiSubCss 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: sbTopics[6]
  name: 'css-service'
  properties: { lockDuration: 'PT5M', maxDeliveryCount: 3 }
}

// ─── Outputs ─────────────────────────────────────────────────────────────────
output namespaceId string = sbNamespace.id
output namespaceFqdn string = '${sbNamespace.name}.servicebus.windows.net'

@description('Location for all resources')
param location string = resourceGroup().location

@description('Resource name')
param name string

// TODO: add parameters, resources, and outputs
