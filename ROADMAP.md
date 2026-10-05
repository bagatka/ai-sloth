# Roadmap

What we build next, in order. A step leaves this list when it lands on main; what a module still
lacks lives in its README's "Not built yet".

Steps 1 to 4, with what is built, are the first release: the product principles in
`ARCHITECTURE.md` hold for every kind of customer, with few integrations of each kind. Everything
after it adds entries to existing lists (a provider, an account kind, a harness) rather than new
concepts.

1. **Checkpoints.** Every turn saves the nook's files and the agent's session in object storage; a
   nook can start from another nook's checkpoint; a new message at 90% disk needs confirmation; each
   person's harness state follows them into new nooks.
2. **Fast start.** Templates (a snapshot taken after setup, refreshed in the background and cached
   where nooks start), so no start waits for a clone or an install, and agents that are ready before
   the message arrives. Measured from Send to the agent's first action.
3. **Hosting.** The official host, hosted nooks isolated with gVisor, self-hosting packaged for a
   VPS or a company network, machines connecting from anywhere, and a push-notification relay any
   host can use.
4. **Apps.** The web app, served by every host, and native iOS and Android apps that connect to any
   number of hosts.

Later, order not decided: folders AiSloth keeps, other git hosts such as GitLab, forks from any turn and recovering a lost agent process, Azure Container
Apps, macOS VMs, automated billing, Projects, and enterprise sign-in extras.
