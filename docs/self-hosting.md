# Self-hosting AiSloth

Run all of AiSloth yourself, inside your own Azure subscription.

## Deploying

`dotnet aspire deploy` deploys to Azure, into one resource group: the WebApi in Container Apps, nooks
in a Container Apps sandbox group, and checkpoints in Blob Storage. Sign in with `az login` first.
It asks for the subscription, resource group, and region, or takes them as `Azure__SubscriptionId`,
`Azure__ResourceGroup`, and `Azure__Location`. Give the AppHost the nook images' public repository
and tag, `Parameters:nook-image-repository` and `Parameters:nook-image-tag`: every push to main
publishes them on GHCR as `ghcr.io/<owner>/aisloth-nook*`, tagged with its commit, so a fork gets
its own. Give `Parameters:postgres-connection-string` to use a Postgres of your own; without it, the
deployment gets an Azure Database for PostgreSQL server.
A deployment also needs three keys that encrypt secrets at rest, `Parameters:agent-accounts-key`,
`-sources-key`, and `-secrets-key`: make each once with `openssl rand -hex 32` and keep them, since
losing one makes what it encrypted unreadable. Aspire keeps every parameter's value in its state,
`~/.aspire/deployments`, and a kept value wins over an environment variable: change one there, or
pass `-- --Parameters:<name>=<value>`. The WebApi prints the first sign-in's setup code in its
log (`az containerapp logs show --name webapi --resource-group <group>`). For a custom domain, add
its records and managed certificate with `az containerapp hostname add` and `bind`, then deploy with
`Parameters:custom-domain` and `Parameters:custom-domain-certificate`, the certificate's name.

## Deploying from GitHub

From GitHub, the `Control plane / Deploy` workflow deploys every push to main once its journeys pass
and its nook images are published; a newer push replaces one still waiting, and a deploy that started
always finishes. Run it by hand in Actions or with `gh workflow run control-plane-deploy.yml` to deploy
main again. Its `production` environment, limited to main, holds the
deployment's settings: the secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and
`AZURE_SUBSCRIPTION_ID` of an Entra app that trusts the environment's GitHub token and may manage
the resource group and assign its roles, `AZURE_RESOURCE_GROUP`, `AZURE_LOCATION`, the three keys as
`AGENT_ACCOUNTS_KEY`, `SOURCES_KEY`, and `SECRETS_KEY`, and `POSTGRES_CONNECTION_STRING`; and the
variables `CUSTOM_DOMAIN`, `CUSTOM_DOMAIN_CERTIFICATE`, and `ALLOW_CHATGPT_PLANS`.
