targetScope = 'resourceGroup'

@description('Azure region for the workload.')
param deploymentLocation string

@description('Environment name used in resource names and tags.')
@minLength(2)
@maxLength(8)
param environmentName string

@description('Short workload name used in resource names.')
@minLength(3)
@maxLength(15)
param workloadName string = 'simplemcpclient'

@description('Workload owner applied to resource tags.')
param owner string

@description('Cost center applied to resource tags.')
param costCenter string

@description('Data classification applied to resource tags.')
param dataClassification string = 'Public'

@description('Container image to deploy when deployApplication is true.')
param image string

@description('Comma-separated exact MCP host names allowed by the web proxy.')
param mcpAllowedHosts string = ''

@secure()
@description('Shared token required to access the hosted web API.')
param webAuthToken string

@description('Create the web application after the image has been pushed.')
param deployApplication bool = false

var tags = {
  env: environmentName
  workload: workloadName
  owner: owner
  costCenter: costCenter
  dataClassification: dataClassification
}
var suffix = uniqueString(subscription().id, resourceGroup().id, workloadName)
var registryName = take('acr${replace(workloadName, '-', '')}${suffix}', 50)
var appName = 'ca-${workloadName}-${environmentName}'
var identityName = 'id-${workloadName}-${environmentName}'
var environmentResourceName = 'cae-${workloadName}-${environmentName}'
var workspaceName = 'log-${workloadName}-${environmentName}'
var insightsName = 'appi-${workloadName}-${environmentName}'
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-07-01' = {
  name: workspaceName
  location: deploymentLocation
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: insightsName
  location: deploymentLocation
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    DisableIpMasking: false
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2025-11-01' = {
  name: registryName
  location: deploymentLocation
  sku: {
    name: 'Basic'
  }
  tags: tags
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: identityName
  location: deploymentLocation
  tags: tags
}

resource pullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, identity.id, acrPullRoleId)
  scope: registry
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
  }
}

resource managedEnvironment 'Microsoft.App/managedEnvironments@2026-07-01' = {
  name: environmentResourceName
  location: deploymentLocation
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
        minimumCount: 0
        maximumCount: 2
      }
    ]
  }
}

resource containerApp 'Microsoft.App/containerApps@2026-07-01' = if (deployApplication) {
  name: appName
  location: deploymentLocation
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: managedEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      secrets: [
        {
          name: 'web-auth-token'
          value: webAuthToken
        }
      ]
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: identity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: workloadName
          image: image
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'MCP_ALLOWED_HOSTS'
              value: mcpAllowedHosts
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: insights.properties.ConnectionString
            }
            {
              name: 'WEB_AUTH_TOKEN'
              secretRef: 'web-auth-token'
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 2
      }
    }
  }
  dependsOn: [
    pullRole
  ]
}

output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output containerAppName string = deployApplication ? containerApp.name : ''
