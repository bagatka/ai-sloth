# Roadmap

What we build next, in order. A step leaves this list when it lands on main; what a module still
lacks lives in its README's "Not built yet".

Steps 1 to 5, with what is built, are the first release: the product principles in
`ARCHITECTURE.md` hold for every kind of customer, with few integrations of each kind. Everything
after it adds entries to existing lists (a provider, an account kind, a harness) rather than new
concepts. Providers for now: Azure Container Apps Sandboxes for the official host, people's own
machines, and local Docker for development, tests, and single-machine self-hosting.

1. **Fast start: instant nooks.** Ready copies that keep their memory where the provider can, as on
   Azure, so a nook starts with its services already running; and a new chat's nook starting while
   its first message is typed, falling asleep and going away when it isn't sent.
2. **Hosting.** The control plane deployed anywhere, object storage in Azure Blob Storage, people's
   own machines connecting from anywhere (Linux, Windows through WSL, Macs through a Linux VM),
   published nook images and CLI binaries, self-hosting packaged for a VPS or a company network,
   nook images from a company's private registry, such as Azure Container Registry through a managed
   identity kept out of its sandboxes, and a push-notification relay any host can use. Anyone who clones runs `./dev`, adds their
   secrets, and runs, tests, or deploys with one command; merging to main deploys the official host
   and its landing page.
3. **Apps.** The web app, served by every host, and native iOS and Android apps that connect to any
   number of hosts.
4. **Working together.** A chat brings in another chat's changes; agents use AiSloth's own API
   (MCP); and projects: groups of chats with members, shared instructions, and a project chat whose
   agent coordinates the others.
5. **New repositories and uploads.** Publish a chat's files as a new GitHub repository, and start a
   chat from an uploaded folder or archive.

Later, order not decided: Macs as machines, with a VM per nook, Linux or macOS for Apple platforms;
a bare-metal provider for cheaper hosted compute; folders AiSloth keeps; forks of a conversation
from any turn; other git hosts such as GitLab; S3-compatible object storage for self-hosting;
automated billing; and enterprise sign-in extras.
