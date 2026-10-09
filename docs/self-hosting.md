# Self-host AiSloth

Run AiSloth on a computer of yours:

- **[On one server or computer](#on-one-server-or-computer):** a server you rent, such as one from
  Hetzner or Hostinger, or your own PC. It takes a few minutes, and you need no accounts or keys.
- **[In your Azure subscription](#in-your-azure-subscription):** nooks run as Azure microVMs, as
  many as you pay for.

## On one server or computer

### What you need

- A server with Ubuntu 24.04 and a public IPv4 address, with ports 80 and 443 open. Take 8 GB of
  memory or more: each awake nook can use up to 4 GB.
- Or your own PC with Linux, or Windows with Ubuntu 24.04 in WSL. Macs come later.
- An x86-64 processor (Intel or AMD). ARM comes next.

Nooks run on this server or PC. Other computers can't run its nooks as machines yet.

### 1. Start AiSloth

On the server, as root, or with `sudo` in front on your PC:

```sh
git clone https://github.com/bagatka/ai-sloth.git
cd ai-sloth
./host up
```

The first time, `./host up` installs Docker and Sysbox, which keeps nooks apart from each other and
from the computer. Then it gets AiSloth and starts it, which takes some minutes the first time. It
ends like this:

```
AiSloth runs at https://203.0.113.5.

Sign in as its first person on your computer, with sloth:
  curl -fsSL https://aisloth.dev/install.sh | sh
  sloth host add https://203.0.113.5 --code 7KQ2-XH4M-PD9T   (valid for a day, once)
```

On a server, AiSloth uses the server's public IP address, with a certificate from Let's Encrypt. On
your PC, it is at `http://localhost:5170`, for that PC only.

### 2. Keep a copy of host.env

`./host up` made the file `host.env` with the host's settings. It holds the key that encrypts the
API keys, tokens, and secrets people save. Copy the file to your password manager or another
computer. Without the key, nobody can read them again.

### 3. Sign in

Run the two commands from the end of `./host up` on your computer. You are now the first person on
your host. Run `sloth help` to see what you can do next.

### Update

```sh
git pull
./host up
```

Add `--build` if you run your own changes. The host restarts in a few seconds, once the model calls
in flight end. Nooks and their agents keep running meanwhile.

### Run your own changes

`./host up --build` builds AiSloth from your checkout, with your changes, instead of getting main's
images. Run it again after each change; it rebuilds only what changed. The first build takes some
minutes and about 4 GB of memory.

### Change settings

Change `host.env`, then run `./host up` again. Its comments say what each setting does:

- **Your own domain:** point the domain's DNS `A` record at the server, then set
  `Host__PublicUrl=https://<your domain>`.
- **GitHub,** so agents work in your repositories and open pull requests: sign in, run
  `sloth github create-app` on your computer, and put the three settings it shows in `host.env`.
- **Sign-in with your company's accounts,** through an OpenID Connect provider such as Microsoft
  Entra ID: set the four `SignIn__Provider__` settings.
- **Other people on your host:** set `Modules__AgentAccounts__AllowChatGptPlans=false`. OpenAI
  allows ChatGPT plans only when you run AiSloth for yourself.

### Other commands

`./host` runs any `docker compose` command for AiSloth:

- `./host logs webapi` shows the host's log.
- `./host down` stops AiSloth, and `./host up` starts it again. Your data stays, and nooks that are
  awake keep running until it starts.

To delete AiSloth from the computer, with its data, nooks, and images, run `./host remove`.

### Other Linux

`./host up` installs Docker and Sysbox only on Ubuntu 24.04. On another Linux, install these
yourself, then run `./host up`:

- Docker Engine 28 or later, with Docker Compose and the
  [containerd image store](https://docs.docker.com/engine/storage/containerd/).
- [Sysbox](https://github.com/nestybox/sysbox) 0.7.1. Its `sysbox-fs` service sometimes stops as it
  starts; this makes systemd start it again:

  ```sh
  sudo mkdir -p /etc/systemd/system/sysbox-fs.service.d && printf '[Service]\nRestart=on-failure\nRestartSec=1\n' | sudo tee /etc/systemd/system/sysbox-fs.service.d/restart.conf
  ```

### If something goes wrong

- **"Docker Desktop can't run Sysbox"**: on Windows, turn off Docker Desktop's WSL integration for
  your Ubuntu (Settings, Resources, WSL integration). Then run `sudo ./host up` in Ubuntu again.
- **"AiSloth runs, but not yet at https://..."**: Let's Encrypt must reach the server on ports 80
  and 443. Open them in your provider's firewall, and for a domain, check its DNS record.
- **A server behind NAT,** such as a VM in AWS or Google Cloud, doesn't know its public address, so
  AiSloth starts at `http://localhost:5170`. Set `Host__PublicUrl` in `host.env` to
  `https://<public address or domain>`, and run `./host up` again.
- **Anything else:** `./host logs webapi` shows what the host did.

## In your Azure subscription

Run all of AiSloth in your own Azure subscription. You do the setup one time. After that, an update
is one command.

### What you get

- **The host:** the AiSloth server, in Azure Container Apps.
- **Nooks:** the computers that agents work in, in an Azure Container Apps sandbox group.
- **Storage:** a PostgreSQL database, and storage for the files that chats save.

All of these go into one Azure resource group. Azure charges you for what they use. A nook uses
compute only while it is awake: it goes to sleep when nobody uses it.

### What you need

- An Azure subscription where you have the **Owner** role. The deploy gives the host access to its
  own resources, and only an Owner can do that.
- A computer with Linux or macOS. On Windows, use WSL.
- About an hour for the first time. Most of it is waiting.

### 1. Get the code and the tools

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

### 2. Sign in to Azure

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

### 3. Make your key

The host encrypts saved secrets, such as API keys, with a key. You make the key one time.

1. Run this command. It shows a new key of 64 letters and digits.

   ```sh
   openssl rand -hex 32
   ```

2. Save the key in your password manager. Name it "AiSloth encryption key".

> **Caution:** Do not lose the key. If you lose it, the host cannot read the data that it
> encrypted, and nobody can get that data back.

### 4. Find the image tag

Nooks start from public images that this repository publishes. The tag of an image is the commit
that made it. Find the newest commit that has images:

```sh
git log -1 --format=%H -- . ':!site' ':!docs' ':!*.md' ':!LICENSE' ':!.github/workflows/site-publish.yml'
```

The command shows a long code, such as `be2ee86eb153ae8c9867596182a25475297790a0`. This is your
image tag. Copy it.

### 5. Deploy

1. Run the deploy command below. First, replace each `<...>` with your values: the image tag from
   step 4, and the key from step 3.

   ```sh
   dotnet aspire deploy -- --Parameters:nook-image-repository=ghcr.io/bagatka --Parameters:nook-image-tag=<image tag> --Parameters:encryption-key=<key>
   ```

2. Answer the questions of the command:
   - Choose your subscription.
   - Type a name for a new resource group, such as `aisloth`.
   - Choose a region near you.
3. Wait until the command ends. The first deploy creates everything, so it takes the longest.

Save your deploy command in your password manager too, because it contains your key. You use it
again for each update.

To use a PostgreSQL server that you have already, add
`--Parameters:postgres-connection-string=<connection string>` to the command. Without it, the deploy
makes an Azure Database for PostgreSQL server for you. The host opens up to 100 connections, the
default of `Maximum Pool Size`, and twice that for a moment during an update, when the new host
starts beside the old one. If your server allows fewer, add `Maximum Pool Size=<number>` to the
connection string, or use your server's connection pooler.

### 6. Sign in

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

### Update your host

1. In the AiSloth shell, get the newest code:

   ```sh
   git pull
   ```

2. Find the new image tag, as in step 4.
3. Run your saved deploy command again, with the new image tag.

### Optional: limit what your host costs

Each nook that is awake is a sandbox you pay for. Your host keeps at most 10 nooks of a workspace
awake at once, puts nooks nobody uses to sleep after two minutes, and lets each person start at most
120 chats and nooks an hour. To be told before costs pass an amount you choose, add a budget to your
resource group in the Azure portal: Cost Management, then Budgets.

### Optional: connect GitHub

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

### Optional: use your own domain

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

### Optional: sign in with your company's accounts

By default, people sign in to the host with codes. To sign them in with an OpenID Connect provider,
such as WorkOS or Microsoft Entra ID, add these settings to your deploy command:

```sh
--Parameters:sign-in-provider-issuer=<issuer URL> --Parameters:sign-in-provider-client-id=<client ID> --Parameters:sign-in-provider-client-secret=<client secret> --Parameters:sign-in-provider-name=<name that people see>
```

### Optional: deploy from GitHub on each push

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
   | `ENCRYPTION_KEY` | Your key |
   | `POSTGRES_CONNECTION_STRING` | Optional: your own PostgreSQL server |

4. Optional: add these variables to the environment:

   | Variable | Value |
   |---|---|
   | `CUSTOM_DOMAIN`, `CUSTOM_DOMAIN_CERTIFICATE` | Your domain and the name of its certificate |
   | `ALLOW_CHATGPT_PLANS` | `false` if other people use your host: OpenAI allows ChatGPT plans only for your own use |

### If something goes wrong

- **"Deploying needs nook-image-repository"**: the command does not have the
  `--Parameters:nook-image-repository` setting. Add it.
- **The deploy asks for a key or stops because a key is missing**: add the
  `--Parameters:encryption-key` setting.
- **A changed setting has no effect**: Aspire remembers your settings in `~/.aspire/deployments`,
  and a remembered value wins over an environment variable. Give the setting in the deploy
  command, after `--`, or change it in that folder.
- **Nooks do not start**: the image tag can be wrong. Do step 4 again, and check that the image
  exists at [github.com/bagatka/ai-sloth/pkgs/container/aisloth-nook](https://github.com/bagatka/ai-sloth/pkgs/container/aisloth-nook).
