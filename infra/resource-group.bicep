targetScope = 'subscription'

@description('Name of the workload resource group.')
param resourceGroupName string

@description('Azure region for the workload.')
param deploymentLocation string

@description('Environment name used in resource tags.')
param environmentName string

@description('Workload owner applied to resource tags.')
param owner string

@description('Cost center applied to resource tags.')
param costCenter string

@description('Data classification applied to resource tags.')
param dataClassification string = 'Public'

resource workloadGroup 'Microsoft.Resources/resourceGroups@2025-04-01' = {
  name: resourceGroupName
  location: deploymentLocation
  tags: {
    env: environmentName
    workload: 'simplemcpclient'
    owner: owner
    costCenter: costCenter
    dataClassification: dataClassification
  }
}

output name string = workloadGroup.name
