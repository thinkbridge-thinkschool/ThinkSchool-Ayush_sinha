targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment that can be used as part of naming resource convention')
param environmentName string

@minLength(1)
@description('Primary location for all resources')
param location string

@minLength(1)
@description('Name of the pre-existing resource group to deploy into. This exercise targets thinkschool-rg, which was already provisioned and verified outside of azd - a new resource group is intentionally not created here to avoid duplicating existing infrastructure.')
param resourceGroupName string

param quotesApiExists bool

@description('Id of the user or app to assign application roles')
param principalId string

@description('Principal type of user or app')
param principalType string

@minLength(1)
@description('Name of the pre-existing Container Apps environment (thinkschool-env) to deploy into, instead of creating a new one.')
param containerAppsEnvironmentName string

@secure()
@minLength(32)
@description('Signing key for QuotesApi locally-issued JWTs (bound to Jwt:Key at runtime). Must be supplied as a secret azd environment value - this exercise does not invent or default a value.')
param jwtSigningKey string

// Tags that should be applied to all resources.
//
// Note that 'azd-service-name' tags should be applied separately to service host resources.
// Example usage:
//   tags: union(tags, { 'azd-service-name': <service name in azure.yaml> })
var tags = {
  'azd-env-name': environmentName
}

// Deploy into the existing resource group (thinkschool-rg) rather than
// creating a new one - see the resourceGroupName parameter description.
resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' existing = {
  name: resourceGroupName
}

module resources 'resources.bicep' = {
  scope: rg
  name: 'resources'
  params: {
    location: location
    tags: tags
    principalId: principalId
    principalType: principalType
    quotesApiExists: quotesApiExists
    containerAppsEnvironmentName: containerAppsEnvironmentName
    jwtSigningKey: jwtSigningKey
  }
}
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT
output AZURE_RESOURCE_QUOTES_API_ID string = resources.outputs.AZURE_RESOURCE_QUOTES_API_ID
