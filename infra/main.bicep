// Deploys drink.it onto an already-existing resource group (rg-drinkit).
// See docs/adr/0007-hosting-en-azure-a-costo-cero.md for the SKU decisions.
targetScope = 'resourceGroup'

param location string = resourceGroup().location

@description('Object id (sid) of the Entra ID user or group that administers Azure SQL.')
param sqlAadAdminObjectId string

@description('Login name (UPN or display name) of the Azure SQL administrator.')
param sqlAadAdminLoginName string

@description('API image, published on ghcr.io. Updated by the deploy workflow on every merge to main.')
param apiContainerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@secure()
@description('JWT signing key. Generate it once and store it as a GitHub secret.')
param jwtSigningKey string

var resourceSuffix = uniqueString(resourceGroup().id)
var sqlServerName = 'sql-drinkit-${resourceSuffix}'
var sqlDatabaseName = 'drinkit'
var storageAccountName = 'stdrinkit${resourceSuffix}'
var storageContainerName = 'product-images'

module logAnalytics 'modules/log-analytics.bicep' = {
  name: 'log-analytics'
  params: {
    location: location
    name: 'law-drinkit'
  }
}

module appInsights 'modules/app-insights.bicep' = {
  name: 'app-insights'
  params: {
    location: location
    name: 'appi-drinkit'
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
  }
}

module identity 'modules/identity.bicep' = {
  name: 'identity'
  params: {
    location: location
    name: 'id-drinkit-api'
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    serverName: sqlServerName
    databaseName: sqlDatabaseName
    aadAdminObjectId: sqlAadAdminObjectId
    aadAdminLoginName: sqlAadAdminLoginName
    tenantId: subscription().tenantId
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    name: storageAccountName
    containerName: storageContainerName
  }
}

module containerAppsEnvironment 'modules/container-apps-environment.bicep' = {
  name: 'container-apps-environment'
  params: {
    location: location
    name: 'cae-drinkit'
    logAnalyticsCustomerId: logAnalytics.outputs.customerId
    logAnalyticsSharedKey: logAnalytics.outputs.primarySharedKey
  }
}

module containerApp 'modules/container-app.bicep' = {
  name: 'container-app'
  params: {
    location: location
    name: 'ca-drinkit-api'
    environmentId: containerAppsEnvironment.outputs.id
    identityId: identity.outputs.id
    identityClientId: identity.outputs.clientId
    containerImage: apiContainerImage
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    storageConnectionString: storage.outputs.connectionString
    storageContainerName: storageContainerName
    jwtSigningKey: jwtSigningKey
    appInsightsConnectionString: appInsights.outputs.connectionString
  }
}

// Static Web Apps can only be created in a fixed handful of regions (brazilsouth isn't
// one of them); it makes no difference for serving content, which is CDN-distributed anyway.
module staticWebApp 'modules/static-web-app.bicep' = {
  name: 'static-web-app'
  params: {
    location: 'eastus2'
    name: 'swa-drinkit'
  }
}

output apiFqdn string = containerApp.outputs.fqdn
output pwaHostname string = staticWebApp.outputs.defaultHostname
output sqlServerFqdn string = sql.outputs.serverFqdn
output storageAccountName string = storage.outputs.name
output apiManagedIdentityPrincipalId string = identity.outputs.principalId
