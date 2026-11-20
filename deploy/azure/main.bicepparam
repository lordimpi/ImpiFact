// Example parameters. Replace the placeholders; never write secret values here: the three secure parameters are read
// from environment variables of the shell that runs the deployment (see README.md).
using 'main.bicep'

param namePrefix = 'impifact'
param imageRegistry = 'myregistry.azurecr.io'
param imageTag = '0.6.0'
param containerRegistryName = 'myregistry'

param databaseEngine = 'Postgres'

param apiHostName = 'api.example.com'
param panelHostName = 'panel.example.com'
param customDomainStage = 'none'

param emailHost = 'smtp.example.com'
param emailFromAddress = 'no-reply@example.com'
param emailUsername = ''

param databaseAdminPassword = readEnvironmentVariable('IMPIFACT_DATABASE_PASSWORD')
param jwtSigningKey = readEnvironmentVariable('IMPIFACT_JWT_SIGNING_KEY')
param emailPassword = readEnvironmentVariable('IMPIFACT_EMAIL_PASSWORD', '')
