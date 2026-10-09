# Roadmap

What we build next, in order. A step leaves this list when it lands on main. Cases the code leaves
unhandled are marked where they would be handled (`// Not handled:`, `PATTERNS.md` entry 11).

Steps 1 to 4, with what is built, are the first release: the product principles in
`ARCHITECTURE.md` hold for every kind of customer, with few integrations of each kind. Everything
after it adds entries to existing lists (a provider, an account kind, a harness) rather than new
concepts. Providers for now: Azure Container Apps Sandboxes for the official host, people's own
machines, and local Docker for development, tests, and single-machine self-hosting.

1. **Hosting.** Nook images from a company's private registry, such as Azure Container Registry
   through a managed identity kept out of its sandboxes, and a push-notification relay any host can
   use. Before people are invited: logs, traces, metrics, and product analytics from the control
   plane and every client in PostHog, through a .NET PostHog SDK of our own that works with Native
   AOT and anyone can use.
2. **Apps.** The web app, served by every host, and native iOS and Android apps that connect to any
   number of hosts.
3. **Working together.** A chat brings in another chat's changes; agents use AiSloth's own API
   (MCP); and projects: groups of chats with members, shared instructions, and a project chat whose
   agent coordinates the others.
4. **New repositories and uploads.** Publish a chat's files as a new GitHub repository, and start a
   chat from an uploaded folder or archive.

Later, order not decided:

- ready copies that keep their memory, once a provider can give a restored copy its own identity
  (Azure keeps the original's labels and environment);
- Macs (below), and a bare-metal provider for cheaper hosted compute;
- resizing a nook whose disk, memory, or CPU runs short;
- machines for hosts on one server, whose nooks reach the host at its public address;
- folders AiSloth keeps, other git hosts such as GitLab, GitHub Enterprise Server, bringing new
  commits into a running nook, and sources from another workspace;
- forks of a conversation from any turn;
- secrets as files, chosen per nook, private chats with a person's own secrets, and a button in the
  apps for pasting a secret an agent asks for;
- leaving a workspace, handing over its management, and a guest's list of the nooks shared with them;
- people's emails, renaming, linking and deleting accounts, ending all of someone's sessions at once,
  and rotating encryption keys;
- sloth for Windows and Intel Macs, with tokens in the system keychain, and commands for nooks
  without a chat;
- S3-compatible object storage for self-hosting, automated billing, and enterprise sign-in extras.

**Macs.** A Mac, registered as a machine or hosting AiSloth itself, runs each nook as a VM of its
own, so it needs no Sysbox: Linux nooks, with Docker inside, through Apple's `container` tool, and
macOS nooks, for Apple platforms, through Tart. Whoever creates a nook on a Mac picks Linux or macOS;
macOS runs at most two macOS VMs at once per Mac, so the app shows how many are free. GitHub's hosted
Mac runners can't start VMs, so the Mac journeys run on a Mac of our own.
