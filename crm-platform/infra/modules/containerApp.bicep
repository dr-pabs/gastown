@description('Name of the Container App')
param appName string

@description('Location for all resources')
param location string = resourceGroup().location

@description('Container Apps Environment resource ID')
param containerAppsEnvironmentId string

@description('Container image to deploy (e.g. myacr.azurecr.io/sfa-service:sha-abc123)')
param containerImage string

@description('Azure Container Registry server (e.g. myacr.azurecr.io)')
param acrServer string

@description('Environment variables to pass to the container')
param envVars array = []

@description('Minimum number of replicas (0 = scale to zero)')
param minReplicas int = 1

@description('Maximum number of replicas')
param maxReplicas int = 10

@description('CPU allocation (e.g. "0.5")')
param cpu string = '0.5'

@description('Memory allocation (e.g. "1.0Gi")')
param memory string = '1.0Gi'

@description('Whether to expose the container app externally via ingress')
param externalIngress bool = false

resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: appName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    environmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        targetPort: 8080
        external: externalIngress
        transport: 'http'
      }
      registries: [
        {
          server: acrServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: appName
          image: containerImage
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: envVars
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              periodSeconds: 30
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 15
              failureThreshold: 3
            }
            {
              type: 'Startup'
              httpGet: {
                path: '/health/start'
                port: 8080
              }
              failureThreshold: 30
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '20'
              }
            }
          }
        ]
      }
    }
  }
}

@description('Managed Identity principal ID — used to assign Key Vault / Service Bus / SQL roles')
output principalId string = containerApp.identity.principalId

@description('Fully qualified domain name of the ingress endpoint')
output fqdn string = containerApp.properties.configuration.ingress.fqdn

@description('Resource name')
output name string = containerApp.name
