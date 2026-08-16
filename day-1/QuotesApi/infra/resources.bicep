@description('The location used for all deployed resources')
param location string = resourceGroup().location

@description('Tags that will be applied to all resources')
param tags object = {}


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
@description('Signing key for QuotesApi locally-issued JWTs (bound to Jwt:Key at runtime).')
param jwtSigningKey string

var abbrs = loadJsonContent('./abbreviations.json')
var resourceToken = uniqueString(subscription().id, resourceGroup().id, location)

// Monitor application with Azure Monitor
module monitoring 'br/public:avm/ptn/azd/monitoring:0.1.0' = {
  name: 'monitoring'
  params: {
    logAnalyticsName: '${abbrs.operationalInsightsWorkspaces}${resourceToken}'
    applicationInsightsName: '${abbrs.insightsComponents}${resourceToken}'
    applicationInsightsDashboardName: '${abbrs.portalDashboards}${resourceToken}'
    location: location
    tags: tags
  }
}
// Container registry
module containerRegistry 'br/public:avm/res/container-registry/registry:0.1.1' = {
  name: 'registry'
  params: {
    name: '${abbrs.containerRegistryRegistries}${resourceToken}'
    location: location
    tags: tags
    publicNetworkAccess: 'Enabled'
    roleAssignments:[
      {
        principalId: quotesApiIdentity.outputs.principalId
        principalType: 'ServicePrincipal'
        roleDefinitionIdOrName: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
      }
    ]
  }
}

// Existing Container Apps environment (thinkschool-env), already provisioned
// and verified outside of azd - reused instead of creating a duplicate
// managed environment inside the same resource group.
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' existing = {
  name: containerAppsEnvironmentName
}

module quotesApiIdentity 'br/public:avm/res/managed-identity/user-assigned-identity:0.2.1' = {
  name: 'quotesApiidentity'
  params: {
    name: '${abbrs.managedIdentityUserAssignedIdentities}quotesApi-${resourceToken}'
    location: location
  }
}
module quotesApiFetchLatestImage './modules/fetch-container-image.bicep' = {
  name: 'quotesApi-fetch-image'
  params: {
    exists: quotesApiExists
    name: 'quotes-api'
  }
}

module quotesApi 'br/public:avm/res/app/container-app:0.8.0' = {
  name: 'quotesApi'
  params: {
    name: 'quotes-api'
    ingressTargetPort: 8080
    ingressExternal: true
    scaleMinReplicas: 1
    scaleMaxReplicas: 10
    secrets: {
      secureList: [
        {
          name: 'jwt-signing-key'
          value: jwtSigningKey
        }
      ]
    }
    containers: [
      {
        image: quotesApiFetchLatestImage.outputs.?containers[?0].?image ?? 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
        name: 'main'
        resources: {
          cpu: json('0.5')
          memory: '1.0Gi'
        }
        env: [
          {
            name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
            value: monitoring.outputs.applicationInsightsConnectionString
          }
          {
            name: 'AZURE_CLIENT_ID'
            value: quotesApiIdentity.outputs.clientId
          }
          {
            name: 'PORT'
            value: '8080'
          }
          {
            // Binds to the app's required "Jwt:Key" configuration value
            // (QuotesApi throws at startup without it - see
            // JwtAuthenticationExtensions.AddDualJwtAuthentication).
            // .NET's environment-variable configuration provider maps
            // double underscores to the ':' section separator.
            name: 'Jwt__Key'
            secretRef: 'jwt-signing-key'
          }
          {
            // Overrides ConnectionStrings:DefaultConnection (default
            // "Data Source=quotes.db", relative to the app's working
            // directory /app). Confirmed via actual container console logs
            // that the deployed container's default non-root user cannot
            // create/open the SQLite file there (SqliteException: SQLite
            // Error 14 'unable to open database file'). /tmp is writable by
            // any user in this image, so redirecting the (still-SQLite,
            // still ephemeral, unchanged otherwise) database file there
            // resolves the crash without touching application code or the
            // database engine itself.
            name: 'ConnectionStrings__DefaultConnection'
            value: 'Data Source=/tmp/quotes.db'
          }
        ]
      }
    ]
    managedIdentities:{
      systemAssigned: false
      userAssignedResourceIds: [quotesApiIdentity.outputs.resourceId]
    }
    registries:[
      {
        server: containerRegistry.outputs.loginServer
        identity: quotesApiIdentity.outputs.resourceId
      }
    ]
    environmentResourceId: containerAppsEnvironment.id
    location: location
    tags: union(tags, { 'azd-service-name': 'quotes-api' })
  }
}
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerRegistry.outputs.loginServer
output AZURE_RESOURCE_QUOTES_API_ID string = quotesApi.outputs.resourceId
