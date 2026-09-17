param location string
param serverName string
param databaseName string
param aadAdminObjectId string
param aadAdminLoginName string
param tenantId string

resource server 'Microsoft.Sql/servers@2023-08-01' = {
  name: serverName
  location: location
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      login: aadAdminLoginName
      sid: aadAdminObjectId
      tenantId: tenantId
      azureADOnlyAuthentication: true
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// administrators.azureADOnlyAuthentication is read-only; this resource is what actually enforces it.
resource aadOnlyAuth 'Microsoft.Sql/servers/azureADOnlyAuthentications@2023-08-01' = {
  parent: server
  name: 'Default'
  properties: {
    azureADOnlyAuthentication: true
  }
}

// Azure Container Apps has no static outbound IP on the Consumption plan, so we allow Azure services.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: server
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: server
  name: databaseName
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    minCapacity: json('0.5')
    autoPauseDelay: 60
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    zoneRedundant: false
  }
}

output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
