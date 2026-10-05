# Roadmap

What we build next, in order. A step leaves this list when it lands on main; what a module still
lacks lives in its README's "Not built yet".

Steps 1 to 6, with what is built, are the first release: the product principles in
`ARCHITECTURE.md` hold for every kind of customer, with few integrations of each kind. Everything
after it adds entries to existing lists (a provider, an account kind, a harness) rather than new
concepts. Providers for now: Azure Container Apps Sandboxes for the official host, people's own
machines, and local Docker for development, tests, and single-machine self-hosting.

1. **Azure provider.** Hosted nooks on Azure Container Apps Sandboxes, microVMs with memory
   snapshots, and a control plane they can reach.
2. **Fast start: ready copies.** A nook whose setup took a while leaves a copy of itself right after
   setup, matched by its setup and harness, that the next nooks, and nooks waking from a long sleep,
   start from and catch up; on Azure a memory snapshot with its services running.
3. **Hosting.** The control plane deployed anywhere, object storage in Azure Blob Storage, people's
   own machines connecting from anywhere (Linux, Windows through WSL, Macs through a Linux VM),
   published nook images and CLI binaries, self-hosting packaged for a VPS or a company network,
   and a push-notification relay any host can use.
4. **Apps.** The web app, served by every host, and native iOS and Android apps that connect to any
   number of hosts.
5. **Working together.** A chat brings in another chat's changes; agents use AiSloth's own API
   (MCP); and projects: groups of chats with members, shared instructions, and a project chat whose
   agent coordinates the others.
6. **New repositories and uploads.** Publish a chat's files as a new GitHub repository, and start a
   chat from an uploaded folder or archive.

Later, order not decided: Macs as machines, with a VM per nook, Linux or macOS for Apple platforms;
a bare-metal provider for cheaper hosted compute; folders AiSloth keeps; forks of a conversation
from any turn; other git hosts such as GitLab; S3-compatible object storage for self-hosting;
automated billing; and enterprise sign-in extras.
