// infra/modules/apiManagement.bicep
// Azure API Management — Layer 1 auth (JWT signature validation) + routing.
// ADR 0005 / ADR 0013: APIM is the single entry point for all external traffic.

@description('Azure region')
param location string

@description('APIM service name (globally unique)')
param apimName string

@description('Publisher email')
param publisherEmail string

@description('Publisher organisation name')
param publisherName string

@description('SKU — Developer (no SLA) for dev, Premium for prod (multi-region, VNet)')
@allowed(['Developer', 'Basic', 'Standard', 'Premium'])
param sku string = 'Developer'

@description('SKU capacity units')
param skuCapacity int = 1

@description('Entra ID tenant ID for JWT validation policy')
param entraTenantId string

@description('Entra ID audience (App Registration client ID)')
param entraAudience string

@description('Tags')
param tags object = {}

resource apim 'Microsoft.ApiManagement/service@2023-05-01-preview' = {
  name: apimName
  location: location
  tags: tags
  sku: {
    name: sku
    capacity: skuCapacity
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    publisherEmail: publisherEmail
    publisherName: publisherName
    virtualNetworkType: sku == 'Premium' ? 'Internal' : 'None'
    customProperties: {
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Protocols.Tls10': 'false'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Protocols.Tls11': 'false'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Backend.Protocols.Ssl30': 'false'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Backend.Protocols.Tls10': 'false'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Backend.Protocols.Tls11': 'false'
    }
  }
}

// ─── Global inbound policy — JWT validation (Layer 1 auth, ADR 0004) ──────────
resource globalPolicy 'Microsoft.ApiManagement/service/policies@2023-05-01-preview' = {
  parent: apim
  name: 'policy'
  properties: {
    value: loadTextContent('../policies/apim-global-policy.xml')
    format: 'xml'
  }
}

output apimId string = apim.id
output apimGatewayUrl string = apim.properties.gatewayUrl
output apimPrincipalId string = apim.identity.principalId
