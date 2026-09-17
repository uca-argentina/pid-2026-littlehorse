param location string
param name string

resource staticSite 'Microsoft.Web/staticSites@2023-12-01' = {
  name: name
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

output defaultHostname string = staticSite.properties.defaultHostname
@secure()
output apiKey string = staticSite.listSecrets().properties.apiKey
