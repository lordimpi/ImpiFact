// ImpiFact on Azure: Container Apps (api, worker, web) plus a manual Container Apps job for the migrator, a managed
// database (PostgreSQL by default; MySQL or Azure SQL), Blob Storage, Key Vault and Application Insights.
// Service map: docs/Arquitectura detallada.md, section 14. Configuration keys: section 11.
//
// Rules: no secret values in this file or in main.bicepparam (secure parameters are read from environment variables
// at deploy time and stored in Key Vault); no storage keys (shared key access is disabled; every workload signs in
// with its user-assigned managed identity); images are parameters.
targetScope = 'resourceGroup'

// ─── Parameters ──────────────────────────────────────────────────────────────────────────────────────────────────────

@description('Short prefix for resource names (lowercase letters, digits and hyphens).')
@minLength(3)
@maxLength(12)
param namePrefix string = 'impifact'

@description('Azure region for every resource.')
param location string = resourceGroup().location

@description('Registry that holds the four images, for example myregistry.azurecr.io or ghcr.io/my-org.')
param imageRegistry string

@description('Tag of the impifact-api, impifact-worker, impifact-migrator and impifact-web images.')
param imageTag string

@description('Name of an Azure Container Registry in this resource group to pull from with managed identity (AcrPull). Empty for a public registry.')
param containerRegistryName string = ''

@description('Gotenberg image (HTML to PDF, ADR-04), run as a sidecar of the api and the worker.')
param gotenbergImage string = 'gotenberg/gotenberg:8.39.0-chromium'

@description('OpenTelemetry Collector image, run as a sidecar of the api and the worker (OTLP to Application Insights).')
param otelCollectorImage string = 'otel/opentelemetry-collector-contrib:0.165.0'

@description('Database engine (Database:Provider).')
@allowed([
  'Postgres'
  'MySql'
  'SqlServer'
])
param databaseEngine string = 'Postgres'

@description('Server version for PostgreSQL or MySQL flexible server (ignored for Azure SQL).')
param databaseVersion string = databaseEngine == 'MySql' ? '8.4' : '17'

@description('Administrator login of the database server; the application connects with it (the Migrator needs db_owner on Azure SQL).')
param databaseAdminLogin string = 'impifact'

@description('Administrator password of the database server: no ";" nor quotes, because it goes inside the connection string. Read from IMPIFACT_DATABASE_PASSWORD in main.bicepparam.')
@secure()
@minLength(16)
param databaseAdminPassword string

@description('JWT signing key (ECDSA P-256 private key in PEM). Read from IMPIFACT_JWT_SIGNING_KEY in main.bicepparam.')
@secure()
param jwtSigningKey string

@description('Public host name of the API, for example api.impifact.co. Same registrable domain as the panel: the session cookie is SameSite=Strict.')
param apiHostName string

@description('Public host name of the panel, for example panel.impifact.co.')
param panelHostName string

@description('Custom domain stage: none (first deploy, create the DNS records from the outputs), validate (add the host names and issue the managed certificates) or bound (TLS bound; keep it for every later deploy).')
@allowed([
  'none'
  'validate'
  'bound'
])
param customDomainStage string = 'none'

@description('SMTP server of the worker (Email:Host).')
param emailHost string

@description('SMTP port (Email:Port).')
param emailPort int = 587

@description('SMTP encryption (Email:Encryption).')
@allowed([
  'none'
  'starttls'
  'ssl'
  'auto'
])
param emailEncryption string = 'starttls'

@description('Sender address (Email:FromAddress).')
param emailFromAddress string

@description('SMTP user name (Email:Username). Empty when the server needs no authentication.')
param emailUsername string = ''

@description('SMTP password (Email:Password); required when emailUsername is set. Read from IMPIFACT_EMAIL_PASSWORD in main.bicepparam.')
@secure()
param emailPassword string = ''

@description('Address space of the virtual network.')
param vnetAddressPrefix string = '10.30.0.0/16'

@description('Container Apps infrastructure subnet. It is also ForwardedHeaders:KnownNetworks: the platform proxy (Envoy) reaches the api from it.')
param appsSubnetPrefix string = '10.30.0.0/23'

@description('Database subnet (delegated to the flexible server, or holding the Azure SQL private endpoint).')
param databaseSubnetPrefix string = '10.30.2.0/24'

@description('Replicas of the api (minimum; HTTP scaling up to apiMaxReplicas).')
@minValue(1)
param apiMinReplicas int = 1

@description('Maximum replicas of the api.')
@minValue(1)
param apiMaxReplicas int = 3

@description('Replicas of the worker (fixed; the outbox and the daily jobs are safe with several).')
@minValue(1)
param workerReplicas int = 1

@description('Resource tags.')
param tags object = {
  application: 'impifact'
}

// ─── Names and derived values ────────────────────────────────────────────────────────────────────────────────────────

var suffix = uniqueString(resourceGroup().id, namePrefix)
var keyVaultName = take('kv-${namePrefix}-${suffix}', 24)
var storageAccountName = take('st${replace(namePrefix, '-', '')}${suffix}', 24)
var documentsContainerName = 'impifact-documents'
var dataProtectionKeyName = 'impifact-data-protection'
var databaseName = 'impifact'
var usePrivateRegistry = !empty(containerRegistryName)
var useEmailCredentials = !empty(emailUsername)

var apiImage = '${imageRegistry}/impifact-api:${imageTag}'
var workerImage = '${imageRegistry}/impifact-worker:${imageTag}'
var migratorImage = '${imageRegistry}/impifact-migrator:${imageTag}'
var webImage = '${imageRegistry}/impifact-web:${imageTag}'

var apiOrigin = 'https://${apiHostName}'
var panelOrigin = 'https://${panelHostName}'

// Endpoints built from the cloud suffixes, without the trailing slash the resource properties carry.
var keyVaultUri = 'https://${keyVaultName}${environment().suffixes.keyvaultDns}'
var blobServiceUri = 'https://${storageAccountName}.blob.${environment().suffixes.storage}'

// Built-in role definition ids.
var roles = {
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  storageBlobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  storageBlobDelegator: 'db58b8e5-c6ad-4a2a-8342-4190687cbf4a'
  keyVaultSecretsOfficer: 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
  keyVaultCryptoUser: '12338af0-0e69-4776-bea7-57ae8d297424'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
}

// ─── Network ─────────────────────────────────────────────────────────────────────────────────────────────────────────

var databaseSubnetDelegations = databaseEngine == 'Postgres'
  ? [
      {
        name: 'postgres'
        properties: { serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers' }
      }
    ]
  : databaseEngine == 'MySql'
      ? [
          {
            name: 'mysql'
            properties: { serviceName: 'Microsoft.DBforMySQL/flexibleServers' }
          }
        ]
      : []

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: 'vnet-${namePrefix}'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: [vnetAddressPrefix] }
    subnets: [
      {
        name: 'snet-apps'
        properties: {
          addressPrefix: appsSubnetPrefix
          delegations: [
            {
              name: 'containerapps'
              properties: { serviceName: 'Microsoft.App/environments' }
            }
          ]
        }
      }
      {
        name: 'snet-database'
        properties: {
          addressPrefix: databaseSubnetPrefix
          delegations: databaseSubnetDelegations
        }
      }
    ]
  }
}

var appsSubnetId = resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, 'snet-apps')
var databaseSubnetId = resourceId('Microsoft.Network/virtualNetworks/subnets', vnet.name, 'snet-database')

var databaseDnsZoneName = databaseEngine == 'Postgres'
  ? '${namePrefix}.private.postgres.database.azure.com'
  : databaseEngine == 'MySql'
      ? '${namePrefix}.private.mysql.database.azure.com'
      : 'privatelink${environment().suffixes.sqlServerHostname}'

resource databaseDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: databaseDnsZoneName
  location: 'global'
  tags: tags
}

resource databaseDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: databaseDnsZone
  name: 'link-${vnet.name}'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: vnet.id }
  }
}

// ─── Database (one of the three, private access only) ────────────────────────────────────────────────────────────────

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = if (databaseEngine == 'Postgres') {
  name: '${namePrefix}-pg-${suffix}'
  location: location
  tags: tags
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: databaseVersion
    administratorLogin: databaseAdminLogin
    administratorLoginPassword: databaseAdminPassword
    storage: { storageSizeGB: 32 }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: { mode: 'Disabled' }
    network: {
      delegatedSubnetResourceId: databaseSubnetId
      privateDnsZoneArmResourceId: databaseDnsZone.id
      publicNetworkAccess: 'Disabled'
    }
  }
  dependsOn: [
    databaseDnsZoneLink
  ]
}

resource postgresDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = if (databaseEngine == 'Postgres') {
  parent: postgres
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource mysql 'Microsoft.DBforMySQL/flexibleServers@2024-12-30' = if (databaseEngine == 'MySql') {
  name: '${namePrefix}-mysql-${suffix}'
  location: location
  tags: tags
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: databaseVersion
    administratorLogin: databaseAdminLogin
    administratorLoginPassword: databaseAdminPassword
    storage: { storageSizeGB: 32 }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: { mode: 'Disabled' }
    network: {
      delegatedSubnetResourceId: databaseSubnetId
      privateDnsZoneResourceId: databaseDnsZone.id
      publicNetworkAccess: 'Disabled'
    }
  }
  dependsOn: [
    databaseDnsZoneLink
  ]
}

resource mysqlDatabase 'Microsoft.DBforMySQL/flexibleServers/databases@2024-12-30' = if (databaseEngine == 'MySql') {
  parent: mysql
  name: databaseName
  properties: {
    charset: 'utf8mb4'
    collation: 'utf8mb4_0900_ai_ci'
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = if (databaseEngine == 'SqlServer') {
  name: '${namePrefix}-sql-${suffix}'
  location: location
  tags: tags
  properties: {
    administratorLogin: databaseAdminLogin
    administratorLoginPassword: databaseAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = if (databaseEngine == 'SqlServer') {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5_1'
    tier: 'GeneralPurpose'
  }
  properties: {
    autoPauseDelay: -1
    minCapacity: json('0.5')
  }
}

resource sqlPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = if (databaseEngine == 'SqlServer') {
  name: 'pe-${namePrefix}-sql'
  location: location
  tags: tags
  properties: {
    subnet: { id: databaseSubnetId }
    privateLinkServiceConnections: [
      {
        name: 'sql'
        properties: {
          privateLinkServiceId: sqlServer.id
          groupIds: ['sqlServer']
        }
      }
    ]
  }
}

resource sqlPrivateEndpointDns 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = if (databaseEngine == 'SqlServer') {
  parent: sqlPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'sql'
        properties: { privateDnsZoneId: databaseDnsZone.id }
      }
    ]
  }
}

// Same formats as deploy/docker-compose.yml, with TLS to the managed server. Azure presents certificates from a public
// CA, so Azure SQL validates the server certificate.
var databaseConnectionString = databaseEngine == 'Postgres'
  ? 'Host=${postgres!.properties.fullyQualifiedDomainName};Port=5432;Database=${databaseName};Username=${databaseAdminLogin};Password=${databaseAdminPassword};SSL Mode=Require;GSS Encryption Mode=Disable'
  : databaseEngine == 'MySql'
      ? 'Server=${mysql!.properties.fullyQualifiedDomainName};Port=3306;Database=${databaseName};User ID=${databaseAdminLogin};Password=${databaseAdminPassword};SslMode=Required'
      : 'Server=tcp:${sqlServer!.properties.fullyQualifiedDomainName},1433;Database=${databaseName};User ID=${databaseAdminLogin};Password=${databaseAdminPassword};Encrypt=True;TrustServerCertificate=False'

// ─── Identities ──────────────────────────────────────────────────────────────────────────────────────────────────────

// api and worker: blobs, certificates and the Data Protection key.
resource workloadIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${namePrefix}-workload'
  location: location
  tags: tags
}

// migrator: only reads the database connection string.
resource migratorIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${namePrefix}-migrator'
  location: location
  tags: tags
}

// web: only pulls its image.
resource webIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${namePrefix}-web'
  location: location
  tags: tags
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = if (usePrivateRegistry) {
  name: usePrivateRegistry ? containerRegistryName : 'unused'
}

resource workloadAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (usePrivateRegistry) {
  name: guid(resourceGroup().id, containerRegistryName, workloadIdentity.id, roles.acrPull)
  scope: containerRegistry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource migratorAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (usePrivateRegistry) {
  name: guid(resourceGroup().id, containerRegistryName, migratorIdentity.id, roles.acrPull)
  scope: containerRegistry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
    principalId: migratorIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource webAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (usePrivateRegistry) {
  name: guid(resourceGroup().id, containerRegistryName, webIdentity.id, roles.acrPull)
  scope: containerRegistry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
    principalId: webIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ─── Blob Storage ────────────────────────────────────────────────────────────────────────────────────────────────────

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

// Storage:AzureBlob:Container: the application does not create it.
resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: documentsContainerName
  properties: { publicAccess: 'None' }
}

// «Storage Blob Data Contributor» on the container and «Storage Blob Delegator» on the account (to sign URLs).
resource blobContributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(documentsContainer.id, workloadIdentity.id, roles.storageBlobDataContributor)
  scope: documentsContainer
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      roles.storageBlobDataContributor
    )
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource blobDelegatorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, workloadIdentity.id, roles.storageBlobDelegator)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDelegator)
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ─── Key Vault ───────────────────────────────────────────────────────────────────────────────────────────────────────

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
  }
}

// Secrets:KeyVault:KeyName: RSA key that wraps the Data Protection keys. Automatic rotation every 90 days: the
// application stores the exact key version that wrapped each key, so older versions keep unwrapping.
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: keyVault
  name: dataProtectionKeyName
  properties: {
    kty: 'RSA'
    keySize: 3072
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
    rotationPolicy: {
      lifetimeActions: [
        {
          trigger: { timeAfterCreate: 'P90D' }
          action: { type: 'rotate' }
        }
      ]
    }
  }
}

// Configuration secrets, written from the secure parameters and read by Container Apps as Key Vault references.
resource databaseConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'database-connection-string'
  properties: { value: databaseConnectionString }
}

resource jwtSigningKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'jwt-signing-key'
  properties: { value: jwtSigningKey }
}

resource emailPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (useEmailCredentials) {
  parent: keyVault
  name: 'email-password'
  properties: { value: emailPassword }
}

// «Key Vault Secrets Officer» and «Key Vault Crypto User» on the vault (certificates as secrets, wrap and unwrap).
resource secretsOfficerAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, workloadIdentity.id, roles.keyVaultSecretsOfficer)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsOfficer)
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource cryptoUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, workloadIdentity.id, roles.keyVaultCryptoUser)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultCryptoUser)
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// The migrator reads one secret only.
resource migratorSecretAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(databaseConnectionSecret.id, migratorIdentity.id, roles.keyVaultSecretsUser)
  scope: databaseConnectionSecret
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
    principalId: migratorIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ─── Observability ───────────────────────────────────────────────────────────────────────────────────────────────────

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${namePrefix}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${namePrefix}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ─── Container Apps environment ──────────────────────────────────────────────────────────────────────────────────────

resource containerEnv 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: 'cae-${namePrefix}'
  location: location
  tags: tags
  properties: {
    vnetConfiguration: {
      infrastructureSubnetId: appsSubnetId
      internal: false
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    // Console logs (JSON, one line per event) go to Log Analytics through a diagnostic setting, without shared keys.
    appLogsConfiguration: { destination: 'azure-monitor' }
  }
}

resource containerEnvDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'logs-to-workspace'
  scope: containerEnv
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
  }
}

var apiCertificateId = '${containerEnv.id}/managedCertificates/cert-api'
var panelCertificateId = '${containerEnv.id}/managedCertificates/cert-panel'

// Stage «validate» adds the host names without TLS so the managed certificates can be issued; stage «bound» binds them.
var apiCustomDomains = customDomainStage == 'none'
  ? []
  : [
      {
        name: apiHostName
        bindingType: customDomainStage == 'bound' ? 'SniEnabled' : 'Disabled'
        certificateId: customDomainStage == 'bound' ? apiCertificateId : null
      }
    ]

var panelCustomDomains = customDomainStage == 'none'
  ? []
  : [
      {
        name: panelHostName
        bindingType: customDomainStage == 'bound' ? 'SniEnabled' : 'Disabled'
        certificateId: customDomainStage == 'bound' ? panelCertificateId : null
      }
    ]

// ─── Shared container settings ───────────────────────────────────────────────────────────────────────────────────────

var registriesFor = {
  workload: usePrivateRegistry
    ? [
        {
          server: containerRegistry!.properties.loginServer
          identity: workloadIdentity.id
        }
      ]
    : []
  migrator: usePrivateRegistry
    ? [
        {
          server: containerRegistry!.properties.loginServer
          identity: migratorIdentity.id
        }
      ]
    : []
  web: usePrivateRegistry
    ? [
        {
          server: containerRegistry!.properties.loginServer
          identity: webIdentity.id
        }
      ]
    : []
}

var workloadSecrets = [
  {
    name: 'database-connection-string'
    keyVaultUrl: '${keyVaultUri}/secrets/database-connection-string'
    identity: workloadIdentity.id
  }
  {
    // Not a credential of the data plane, but kept out of plain environment values.
    name: 'appinsights-connection-string'
    value: appInsights.properties.ConnectionString
  }
]

// Configuration shared by the api and the worker (docs/Arquitectura detallada.md, section 11).
var backendEnv = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
  { name: 'Database__Provider', value: databaseEngine }
  { name: 'Database__ConnectionString', secretRef: 'database-connection-string' }
  { name: 'Storage__Provider', value: 'AzureBlob' }
  { name: 'Storage__AzureBlob__ServiceUri', value: blobServiceUri }
  { name: 'Storage__AzureBlob__Container', value: documentsContainerName }
  { name: 'Secrets__Provider', value: 'KeyVault' }
  { name: 'Secrets__KeyVault__VaultUri', value: keyVaultUri }
  { name: 'Secrets__KeyVault__KeyName', value: dataProtectionKeyName }
  // DefaultAzureCredential picks the user-assigned identity by its client id.
  { name: 'AZURE_CLIENT_ID', value: workloadIdentity.properties.clientId }
  // Gotenberg and the collector are sidecars: same network namespace.
  { name: 'Pdf__GotenbergUrl', value: 'http://127.0.0.1:3000' }
  { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: 'http://127.0.0.1:4317' }
]

var sidecars = [
  {
    name: 'gotenberg'
    image: gotenbergImage
    resources: {
      cpu: json('0.5')
      memory: '1Gi'
    }
  }
  {
    name: 'otel-collector'
    image: otelCollectorImage
    args: ['--config=env:OTELCOL_CONFIG']
    env: [
      { name: 'OTELCOL_CONFIG', value: loadTextContent('otel-collector.yaml') }
      { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights-connection-string' }
    ]
    resources: {
      cpu: json('0.25')
      memory: '0.5Gi'
    }
  }
]

func healthProbes(port int) array => [
  {
    type: 'Startup'
    httpGet: {
      path: '/health/live'
      port: port
    }
    periodSeconds: 10
    timeoutSeconds: 5
    failureThreshold: 10
  }
  {
    type: 'Liveness'
    httpGet: {
      path: '/health/live'
      port: port
    }
    periodSeconds: 15
    timeoutSeconds: 5
    failureThreshold: 3
  }
  {
    type: 'Readiness'
    httpGet: {
      path: '/health/ready'
      port: port
    }
    periodSeconds: 15
    timeoutSeconds: 10
    failureThreshold: 3
  }
]

// ─── api ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

resource apiApp 'Microsoft.App/containerApps@2025-01-01' = {
  name: '${namePrefix}-api'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${workloadIdentity.id}': {} }
  }
  properties: {
    environmentId: containerEnv.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      // TLS terminates at the platform ingress; the container speaks HTTP on 8080.
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
        customDomains: apiCustomDomains
      }
      registries: registriesFor.workload
      secrets: concat(workloadSecrets, [
        {
          name: 'jwt-signing-key'
          keyVaultUrl: '${keyVaultUri}/secrets/jwt-signing-key'
          identity: workloadIdentity.id
        }
      ])
    }
    template: {
      containers: concat(
        [
          {
            name: 'api'
            image: apiImage
            env: concat(backendEnv, [
              { name: 'Jwt__SigningKey', secretRef: 'jwt-signing-key' }
              { name: 'Cors__Origins__0', value: panelOrigin }
              // The platform proxy (Envoy) connects from the infrastructure subnet: trust its X-Forwarded-For so
              // the login rate limit sees the real client IP.
              { name: 'ForwardedHeaders__KnownNetworks__0', value: appsSubnetPrefix }
            ])
            probes: healthProbes(8080)
            resources: {
              cpu: json('0.5')
              memory: '1Gi'
            }
          }
        ],
        sidecars
      )
      scale: {
        minReplicas: apiMinReplicas
        maxReplicas: apiMaxReplicas
      }
    }
  }
  // The workload identity needs its roles before the containers start.
  dependsOn: [
    blobContributorAssignment
    blobDelegatorAssignment
    secretsOfficerAssignment
    cryptoUserAssignment
    workloadAcrPull
    jwtSigningKeySecret
    databaseConnectionSecret
  ]
}

// ─── worker ──────────────────────────────────────────────────────────────────────────────────────────────────────────

resource workerApp 'Microsoft.App/containerApps@2025-01-01' = {
  name: '${namePrefix}-worker'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${workloadIdentity.id}': {} }
  }
  properties: {
    environmentId: containerEnv.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      // No ingress: nobody calls the worker. The probes reach its health port (8091) directly.
      registries: registriesFor.workload
      secrets: concat(
        workloadSecrets,
        useEmailCredentials
          ? [
              {
                name: 'email-password'
                keyVaultUrl: '${keyVaultUri}/secrets/email-password'
                identity: workloadIdentity.id
              }
            ]
          : []
      )
    }
    template: {
      containers: concat(
        [
          {
            name: 'worker'
            image: workerImage
            env: concat(
              backendEnv,
              [
                { name: 'Panel__BaseUrl', value: panelOrigin }
                { name: 'Email__Host', value: emailHost }
                { name: 'Email__Port', value: string(emailPort) }
                { name: 'Email__Encryption', value: emailEncryption }
                { name: 'Email__FromAddress', value: emailFromAddress }
              ],
              useEmailCredentials
                ? [
                    { name: 'Email__Username', value: emailUsername }
                    { name: 'Email__Password', secretRef: 'email-password' }
                  ]
                : []
            )
            probes: healthProbes(8091)
            resources: {
              cpu: json('0.5')
              memory: '1Gi'
            }
          }
        ],
        sidecars
      )
      scale: {
        minReplicas: workerReplicas
        maxReplicas: workerReplicas
      }
    }
  }
  // The workload identity needs its roles before the containers start.
  dependsOn: [
    blobContributorAssignment
    blobDelegatorAssignment
    secretsOfficerAssignment
    cryptoUserAssignment
    workloadAcrPull
    databaseConnectionSecret
    emailPasswordSecret
  ]
}

// ─── web (panel) ─────────────────────────────────────────────────────────────────────────────────────────────────────

resource webApp 'Microsoft.App/containerApps@2025-01-01' = {
  name: '${namePrefix}-web'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${webIdentity.id}': {} }
  }
  properties: {
    environmentId: containerEnv.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
        customDomains: panelCustomDomains
      }
      registries: registriesFor.web
    }
    template: {
      containers: [
        {
          name: 'web'
          image: webImage
          env: [
            // The browser calls the API directly (CORS with credentials; Arquitectura detallada, section 14).
            { name: 'IMPIFACT_API_BASE_URL', value: apiOrigin }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              periodSeconds: 15
              timeoutSeconds: 5
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              periodSeconds: 15
              timeoutSeconds: 5
              failureThreshold: 3
            }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 2
      }
    }
  }
  dependsOn: [
    webAcrPull
  ]
}

// Managed certificates, issued in stage «validate» once the DNS records exist (CNAME to the app and TXT asuid.<host>).
resource apiCertificate 'Microsoft.App/managedEnvironments/managedCertificates@2025-01-01' = if (customDomainStage == 'validate') {
  parent: containerEnv
  name: 'cert-api'
  location: location
  tags: tags
  properties: {
    subjectName: apiHostName
    domainControlValidation: 'CNAME'
  }
  dependsOn: [
    apiApp
  ]
}

resource panelCertificate 'Microsoft.App/managedEnvironments/managedCertificates@2025-01-01' = if (customDomainStage == 'validate') {
  parent: containerEnv
  name: 'cert-panel'
  location: location
  tags: tags
  properties: {
    subjectName: panelHostName
    domainControlValidation: 'CNAME'
  }
  dependsOn: [
    webApp
  ]
}

// ─── migrator (pre-deploy job) ───────────────────────────────────────────────────────────────────────────────────────

// Manual job: run it before each release, «az containerapp job start», and wait for it to succeed (README).
resource migratorJob 'Microsoft.App/jobs@2025-01-01' = {
  name: '${namePrefix}-migrator'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${migratorIdentity.id}': {} }
  }
  properties: {
    environmentId: containerEnv.id
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1800
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: registriesFor.migrator
      secrets: [
        {
          name: 'database-connection-string'
          keyVaultUrl: '${keyVaultUri}/secrets/database-connection-string'
          identity: migratorIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrator'
          image: migratorImage
          env: [
            { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
            { name: 'Database__Provider', value: databaseEngine }
            { name: 'Database__ConnectionString', secretRef: 'database-connection-string' }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
    }
  }
  dependsOn: [
    migratorSecretAssignment
    migratorAcrPull
  ]
}

// ─── Outputs ─────────────────────────────────────────────────────────────────────────────────────────────────────────

@description('CNAME target of the API host name.')
output apiDefaultFqdn string = apiApp.properties.configuration.ingress.fqdn

@description('CNAME target of the panel host name.')
output panelDefaultFqdn string = webApp.properties.configuration.ingress.fqdn

@description('Value of the TXT records asuid.<api host> and asuid.<panel host>.')
output customDomainVerificationId string = containerEnv.properties.customDomainConfiguration.customDomainVerificationId

@description('Job to start before each release.')
output migratorJobName string = migratorJob.name

@description('Key Vault (Secrets:KeyVault:VaultUri).')
output keyVaultUri string = keyVaultUri

@description('Blob service (Storage:AzureBlob:ServiceUri).')
output blobServiceUri string = blobServiceUri
