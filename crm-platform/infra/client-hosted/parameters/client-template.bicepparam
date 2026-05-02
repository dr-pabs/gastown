// Client-Hosted Deployment Parameter Template
// Copy this file and rename to <client-id>.bicepparam for each client deployment.
// Fill in all TODO values before running cd-client-hosted.yml pipeline.

using '../main.bicep'

param environment = 'prod'
param location = 'uksouth'           // TODO: set to client's preferred region
param resourceGroupName = 'crm-client-TODO-rg'
param imageTag = 'latest'

// TODO: Add client-specific parameters (tenant name, user count tier, etc.)
