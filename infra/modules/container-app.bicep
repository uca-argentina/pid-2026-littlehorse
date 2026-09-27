param location string
param name string
param environmentId string
param identityId string
param identityClientId string
param containerImage string
param sqlServerFqdn string
param sqlDatabaseName string
@secure()
param storageConnectionString string
param storageContainerName string
@secure()
param jwtSigningKey string
@secure()
param appInsightsConnectionString string
@secure()
param bootstrapAdminPassword string
param registryUsername string
@secure()
param registryPassword string

// Only until the first administrator exists: Container Apps rejects a secret with
// an empty value, and without the variable the API seeds nothing.
var bootstrapSecrets = empty(bootstrapAdminPassword)
  ? []
  : [{ name: 'bootstrap-admin-password', value: bootstrapAdminPassword }]
var bootstrapEnv = empty(bootstrapAdminPassword)
  ? []
  : [{ name: 'Bootstrap__AdminPassword', secretRef: 'bootstrap-admin-password' }]

// Only while the image on ghcr.io is private: Container Apps cannot pull from it
// with a managed identity, so it signs in with a GitHub token. Empty means a
// public image, which needs no credentials at all.
var registrySecrets = empty(registryPassword)
  ? []
  : [{ name: 'ghcr-pull-token', value: registryPassword }]
var registries = empty(registryPassword)
  ? []
  : [{ server: 'ghcr.io', username: registryUsername, passwordSecretRef: 'ghcr-pull-token' }]

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      secrets: concat([
        { name: 'image-storage-connection-string', value: storageConnectionString }
        { name: 'jwt-signing-key', value: jwtSigningKey }
        { name: 'appinsights-connection-string', value: appInsightsConnectionString }
      ], bootstrapSecrets, registrySecrets)
      registries: registries
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: concat([
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            {
              name: 'ConnectionStrings__DrinkIt'
              value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${identityClientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
            }
            { name: 'ImageStorage__ConnectionString', secretRef: 'image-storage-connection-string' }
            { name: 'ImageStorage__ContainerName', value: storageContainerName }
            { name: 'Jwt__SigningKey', secretRef: 'jwt-signing-key' }
            { name: 'Jwt__Issuer', value: 'drinkit' }
            { name: 'Jwt__Audience', value: 'drinkit' }
            { name: 'Jwt__LifetimeMinutes', value: '480' }
            { name: 'ApplicationInsights__ConnectionString', secretRef: 'appinsights-connection-string' }
          ], bootstrapEnv)
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 2
      }
    }
  }
}

output fqdn string = containerApp.properties.configuration.ingress.fqdn
