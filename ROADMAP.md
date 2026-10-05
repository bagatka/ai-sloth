# Roadmap

What we build next, in order. A step leaves this list when it lands on main; what a module still
lacks lives in its README's "Not built yet".

Steps 1 and 2 give the first real use: a person signs in with the `sloth` CLI and works with an
agent on a ChatGPT plan, with the control plane running locally and nooks in the local Docker
Engine. Step 5 brings the same experience to a deployed control plane, with people's own computers
as machines.

1. **Codex and pi harnesses.** Profiles and nook images for `codex-acp` and `pi-acp`; ChatGPT plans
   as personal agent accounts, behind a setting like Claude subscriptions until OpenAI allows them
   for hosted apps.
2. **CLI.** `sloth` signs in and covers nooks, agent accounts, and chats: streamed output, steering,
   queued messages, and stop. People's names, taken from sign-in, come with it.
3. **Sources.** Repositories mounted at `/work/<name>`; agents push branches through the control
   plane's git proxy.
4. **Checkpoints.** Object storage; every turn saves the nook's files and the agent's session; a new
   message at 90% disk needs confirmation; each person's harness state is saved and restored into
   their new nooks.
5. **Deploy.** The control plane on AWS or Azure, nook images in a registry, people's computers
   registered as machines.
6. **Forks and recovery.** Restart a conversation from any turn; survive a lost agent process.
7. **Azure Container Apps.** A sandbox provider next to Docker and machines.
8. **macOS VMs.** Nooks on Macs as virtual machines, starting with a spike.

Later, order not decided: the web app.
