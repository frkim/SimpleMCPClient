using './main.bicep'

param deploymentLocation = 'eastus'
param environmentName = 'dev'
param workloadName = 'simplemcpclient'
param owner = 'frkim'
param costCenter = 'shared'
param dataClassification = 'Public'
param image = 'mcr.microsoft.com/k8se/quickstart:latest'
param mcpAllowedHosts = ''
param webAuthToken = readEnvironmentVariable('WEB_AUTH_TOKEN')
param deployApplication = false
