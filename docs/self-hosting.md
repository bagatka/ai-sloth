# Self-host AiSloth

You can run all of AiSloth in your own Azure subscription. You do the setup one time. After that,
an update is one command.

## What you get

- **The host:** the AiSloth server, in Azure Container Apps.
- **Nooks:** the computers that agents work in, in an Azure Container Apps sandbox group.
- **Storage:** a PostgreSQL database, and storage for the files that chats save.

All of these go into one Azure resource group. Azure charges you for what they use. A nook uses
compute only while it is awake: it goes to sleep when nobody uses it.

## What you need

- An Azure subscription where you have the **Owner** role. The deploy gives the host access to its
  own resources, and only an Owner can do that.
- A computer with Linux or macOS. On Windows, use WSL.
- About an hour for the first time. Most of it is waiting.

## 1. Get the code and the tools

1. Install Nix. Follow the steps for your system at [nixos.org/download](https://nixos.org/download/).
2. Open a terminal, and get the code:

   ```sh
   git clone https://github.com/bagatka/ai-sloth.git
   cd ai-sloth
   ```

3. Open the AiSloth shell:

   ```sh
   ./dev
   ```

   The first time, this downloads .NET, the Azure CLI, and the other tools. It can take some
   minutes. Do all the next steps in this shell.

4. Install the deploy tool, Aspire:

   ```sh
   dotnet tool restore
   ```

## 2. Sign in to Azure

1. Sign in:

   ```sh
   az login
   ```

   A browser opens. Sign in with your Azure account. If you have more than one subscription, the
   command asks which one to use.

2. Install Bicep, which Azure uses to create resources:

   ```sh
   az bicep install
   ```

## 3. Make your three keys

The host encrypts saved secrets, such as API keys, with three keys. You make the keys one time.

1. Run this command three times. Each time, it shows a new key of 64 letters and digits.

   ```sh
   openssl rand -hex 32
   ```

2. Save the three keys in your password manager. Name them "agent accounts key", "sources key", and
   "secrets key".

> **Caution:** Do not lose the keys. If you lose a key, the host cannot read the data that the key
> encrypted, and nobody can get that data back.

## 4. Find the image tag

Nooks start from public images that this repository publishes. The tag of an image is the commit
that made it. Find the newest commit that has images:

```sh
git log -1 --format=%H -- . ':!site' ':!docs' ':!*.md' ':!LICENSE' ':!.github/workflows/site-publish.yml'
```

The command shows a long code, such as `be2ee86eb153ae8c9867596182a25475297790a0`. This is your
image tag. Copy it.

## 5. Deploy

1. Run the deploy command below. First, replace each `<...>` with your values: the image tag from
   step 4, and the three keys from step 3.

   ```sh
   dotnet aspire deploy -- --Parameters:nook-image-repository=ghcr.io/bagatka --Parameters:nook-image-tag=<image tag> --Parameters:agent-accounts-key=<agent accounts key> --Parameters:sources-key=<sources key> --Parameters:secrets-key=<secrets key>
   ```

2. Answer the questions of the command:
   - Choose your subscription.
   - Type a name for a new resource group, such as `aisloth`.
   - Choose a region near you.
3. Wait until the command ends. The first deploy creates everything, so it takes the longest.

Save your deploy command in your password manager too, because it contains your keys. You use it
again for each update.

To use a PostgreSQL server that you have already, add
`--Parameters:postgres-connection-string=<connection string>` to the command. Without it, the deploy
makes an Azure Database for PostgreSQL server for you.

## 6. Sign in

1. Get the log of the host. Replace `<resource group>` with the name from step 5.

   ```sh
   az containerapp logs show --name webapi --resource-group <resource group> --tail 300 --format text
   ```

2. Find the line that starts with `First sign-in:`. It contains a complete command:
   `sloth host add https://<address of your host> --code <setup code>`.
3. Install sloth, the AiSloth command line, if you do not have it:

   ```sh
   curl -fsSL https://aisloth.dev/install.sh | sh
   ```

4. Run the `sloth host add` command from the log. You are now the first person on your host.

> **Note:** The setup code works one time, for one day.

Run `sloth help` to see what you can do next.

## Update your host

1. In the AiSloth shell, get the newest code:

   ```sh
   git pull
   ```

2. Find the new image tag, as in step 4.
3. Run your saved deploy command again, with the new image tag.

## Optional: connect GitHub

Do this so that agents can work in your GitHub repositories and open pull requests.

1. Make a GitHub App for your host:

   ```sh
   sloth github create-app
   ```

   A browser opens at GitHub. Confirm the app. Then the command shows three values: a client ID, a
   client secret, and a slug.

2. On GitHub, open the settings of the new app. Select **Enable Device Flow**, and save.
3. Add these three settings to your deploy command, and run it again:

   ```sh
   --Parameters:github-app-client-id=<client ID> --Parameters:github-app-client-secret=<client secret> --Parameters:github-app-slug=<slug>
   ```

## Optional: use your own domain

Do this to give your host an address such as `aisloth.example.com`. Replace `<resource group>` and
`<domain>` in each command.

1. Find the address of the host, and the verification code of your Azure environment:

   ```sh
   az containerapp show --name webapi --resource-group <resource group> --query properties.configuration.ingress.fqdn --output tsv
   az containerapp show --name webapi --resource-group <resource group> --query properties.customDomainVerificationId --output tsv
   ```

2. At your DNS provider, add two records:
   - A `CNAME` record from your domain to the address of the host.
   - A `TXT` record with the name `asuid.<domain>` and the verification code as its value.
3. Add the domain to the host, and make a free certificate for it:

   ```sh
   az containerapp hostname add --name webapi --resource-group <resource group> --hostname <domain>
   az containerapp env list --resource-group <resource group> --query "[].name" --output tsv
   az containerapp hostname bind --name webapi --resource-group <resource group> --hostname <domain> --environment <environment> --validation-method CNAME
   ```

   The second command shows the name of your environment. Use it in the third command.

4. Find the name of the certificate:

   ```sh
   az containerapp env certificate list --name <environment> --resource-group <resource group> --query "[].name" --output tsv
   ```

5. Add these two settings to your deploy command, and run it again:

   ```sh
   --Parameters:custom-domain=<domain> --Parameters:custom-domain-certificate=<certificate name>
   ```

## Optional: sign in with your company's accounts

By default, people sign in to the host with codes. To sign them in with an OpenID Connect provider,
such as WorkOS or Microsoft Entra ID, add these settings to your deploy command:

```sh
--Parameters:sign-in-provider-issuer=<issuer URL> --Parameters:sign-in-provider-client-id=<client ID> --Parameters:sign-in-provider-client-secret=<client secret> --Parameters:sign-in-provider-name=<name that people see>
```

## Optional: deploy from GitHub on each push

The `Control plane / Deploy` workflow deploys a fork's `main` branch after each push, when its tests
pass and its nook images are ready. Pushes that change only the landing page or the docs skip it. To
deploy again without a push, run the workflow in Actions, or run
`gh workflow run control-plane-deploy.yml`.

1. In Microsoft Entra ID, make an app that trusts the GitHub token of your repository's `production`
   environment. Give it the rights to manage the resource group and to assign roles in it.
2. In your repository, make an environment with the name `production`. Allow only `main` to deploy
   to it.
3. Add these secrets to the environment:

   | Secret | Value |
   |---|---|
   | `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` | The IDs of the Entra app, its tenant, and your subscription |
   | `AZURE_RESOURCE_GROUP`, `AZURE_LOCATION` | Your resource group and its region |
   | `AGENT_ACCOUNTS_KEY`, `SOURCES_KEY`, `SECRETS_KEY` | Your three keys |
   | `POSTGRES_CONNECTION_STRING` | Optional: your own PostgreSQL server |

4. Optional: add these variables to the environment:

   | Variable | Value |
   |---|---|
   | `CUSTOM_DOMAIN`, `CUSTOM_DOMAIN_CERTIFICATE` | Your domain and the name of its certificate |
   | `ALLOW_CHATGPT_PLANS` | `false` if other people use your host: OpenAI allows ChatGPT plans only for your own use |

## If something goes wrong

- **"Deploying needs nook-image-repository"**: the command does not have the
  `--Parameters:nook-image-repository` setting. Add it.
- **The deploy asks for a key or stops because a key is missing**: add the three key settings.
- **A changed setting has no effect**: Aspire remembers your settings in `~/.aspire/deployments`,
  and a remembered value wins over an environment variable. Give the setting in the deploy
  command, after `--`, or change it in that folder.
- **Nooks do not start**: the image tag can be wrong. Do step 4 again, and check that the image
  exists at [github.com/bagatka/ai-sloth/pkgs/container/aisloth-nook](https://github.com/bagatka/ai-sloth/pkgs/container/aisloth-nook).
