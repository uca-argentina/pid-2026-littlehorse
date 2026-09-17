param location string
param name string
param logAnalyticsWorkspaceId string

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: name
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspaceId
    RetentionInDays: 30
  }
}

@secure()
output connectionString string = appInsights.properties.ConnectionString
