# Glossary

One name per concept, used the same way in code, APIs, storage, UI, and conversation.

- Don't introduce synonyms.
- Don't reuse a name for something else.
- Add terms in the same change that introduces them.

## Architecture terms

| Term | Meaning | Lives in | Don't call it |
|---|---|---|---|
| Module | A capability with one contract, its own data, and an internal implementation | `src/ControlPlane/Modules/<Module>` | service (until extracted), component, domain |
| Contract | A module's public interface `I<Module>Api` and its records | `<Module>.Contracts` | facade, client, port |
| Feature | One contract method, implemented in one file | `Features/<Module>Api.<Feature>.cs` | use case, handler, command handler |
| Command | Record carrying input to a state-changing feature, named verb plus object (`RenameUser`) | Contracts | request, DTO |
| DTO | Record a contract returns | Contracts | model, view model, response |
| Request | HTTP input record in the WebApi | `Endpoints/` | command |
| WebApi | The HTTP host and composition root of the control plane | `Bagatka.AiSloth.WebApi` | gateway, API layer, BFF, controllers |
| Gateway | The future public edge in front of several services; routes public endpoints and hides internal ones. It doesn't exist yet. | — | the WebApi |
| Composition | A WebApi response assembled from several modules | `Composition/` | aggregator, orchestrator |
| Actor | Who performs a call: a user, a named system process, or anonymous | `Bagatka.Foundation` | current user, principal, caller |
| Entity | Persisted object that owns its state changes and rules | `Model/` | model, aggregate |
| Value type | Small immutable type that owns a validation rule | `Model/` | value object, wrapper |
| Typed ID | Strongly typed identifier of an entity | Contracts | key, raw Guid |
| Result / Error | Returned value for expected outcomes; an error has a kind and a code | `Bagatka.Foundation` | exception |
| Integration event | A fact a module publishes after commit | Contracts | domain event, message, notification |
| Reaction | A module's handler for another module's event | `Reactions/` | consumer, subscriber, listener |
| Outbox | Where a module's events go: entity methods receive `IOutbox` and add events, which commit in the same transaction as the change | module schema | queue, event bus |
| Job | Background work owned by a module | `Jobs/` | worker, cron, task |
| Foundation | General-purpose plumbing shared by every project | `src/Foundation` | platform, shared kernel, common, core, utils |
| Sdk client | General-purpose client for a third-party API | `src/Sdk` | integration, adapter, wrapper |
| Settings | Fixed values an owner needs: an immutable record the host builds from configuration and passes at registration | next to the owner's registration | options, config |
| Live options | Values that may change while running, read through `IOptionsMonitor<T>` at each decision | the owner's project | settings, dynamic config |

## Product terms

| Term | Meaning | Owner | Don't call it |
|---|---|---|---|
| Control plane | The system that owns all state and decisions: the WebApi and its modules, later several services | `src/ControlPlane` | platform, backend, engine |
| Host | A running control plane people sign in to, known by its address: the official one, someone's VPS, a company's | WebApi | server, instance, deployment (for the running thing) |
| User | A person who signs in. One user can have access to many workspaces and nooks. | Users | account, customer |
| Session | One device's sign-in to a host: an opaque token the host issued, kept only as a hash, ending when signed out or after 90 days unused | Users | login, JWT, access token |
| Setup code | The one-time code a host prints while nobody has signed up; whoever uses it first becomes the host's first person | Users | bootstrap token, admin password |
| Link code | A one-time code from a signed-in device that signs the same person in on another, within 10 minutes | Users | pairing code, device code |
| Sign-in provider | An OpenID Connect provider a host may sign people in with, such as WorkOS or Entra ID; the host is its client | WebApi | IdP, auth server, issuer (for the provider) |
| Workspace | Where people work together and what owns nooks, like a Slack workspace: a personal one, a company one | Workspaces | organization, team, tenant |
| Member | Someone given access to a workspace | Workspaces | participant, seat |
| Access level | How much a person may do with a resource: Read (see), Write (work), or Manage (decide who else has access). Each includes the ones below it. | Workspaces | role, permission |
| Resource | Something people are given access to: a workspace or a nook, later a project. Access to it reaches everything in it. | Workspaces | place, scope, space |
| Grant | One person's access level on one resource, given directly | Workspaces | membership, ACL entry |
| Invite | A one-time code that gives whoever accepts it first an access level on a resource, for 7 days | Workspaces | invitation, link |
| Guest | Someone with access to a nook but not to its workspace: they watch it and propose in its chat, but never operate it | Workspaces | external user |
| Nook | Where an agent works: an isolated machine with its files and processes, owned by a workspace, created by its chat; with it, the basic unit. A nook without a chat runs processes only. A provider's sandbox underneath. | Nooks | sandbox, orb, VM, container, environment, workspace |
| Reserved nook | A nook only one person may operate, besides the control plane: run, feed, and stop its processes and move its files. A chat on a personal account reserves its nook for that account's owner; any other nook is operated by everyone with Write on its workspace | Nooks | private nook, owned nook, operator |
| Chat | A conversation between people and one coding agent, in the nook the chat creates for it; one chat per nook | Chats | thread, session, conversation |
| Turn | One message in a chat and everything the agent did in reply, ending with a stop reason | Chats | step, exchange |
| Draft | A chat nobody wrote in yet, started as someone starts writing so its nook is ready when they send; not listed, and gone with its nook after a while unsent | Chats | — |
| Steering | A message sent during a turn going into that turn, so the agent reads it while it works | Chats | interrupt, injection |
| Stop | Ending the running turn at once; messages the agent hasn't received are cancelled | Chats | cancel (in the product), abort, interrupt |
| Model gateway | The WebApi endpoint agents call their model through; it forwards each call to their chat's agent account's endpoint with the headers that pay for it, so no nook holds a key or a plan's token | WebApi, Chats | LLM proxy, API proxy |
| Checkpoint | A nook's files saved at one moment, such as after each turn: `/work` and its repositories with their history, honoring `.gitignore`, and its kept paths; numbered in its nook, kept while the nook exists | Nooks | snapshot, backup, save point |
| Kept paths | Paths outside `/work` a nook's checkpoints keep too, such as where its agent keeps its sessions | Nooks | extra paths, volumes |
| Fork | A new chat continuing another's conversation from one of its turns, in a nook started from the checkpoint after it | Nooks, Chats (planned) | clone, branch |
| Source | Where some of a nook's files come from: a repository, later a folder. A nook holds a copy of each at `/work/<name>`, named after it | Sources, Nooks | repo (for both kinds), mount, folder (for a repository) |
| Repository | A GitHub repository a workspace connected, which its chats' nooks can start with | Sources | repo link, git source, project |
| GitHub connection | A person's GitHub account, connected through the host's GitHub App; AiSloth copies repositories in and pushes changes as them with it, and no nook ever holds it | Sources | GitHub integration, token, login |
| GitHub App | The app on GitHub each host has, which people connect their account through and install on their repositories | Sources | OAuth app, bot (for the app) |
| Push | Sending a nook's changes in a source to a branch on GitHub, opening a pull request when asked: never to the default branch, and only ever moving a branch forward | Sources, WebApi | sync, upload, deploy |
| Git settings | How a person's commits and branches look: author, committer, AiSloth as co-author, and branch prefix | Sources | git config, identity (alone) |
| Folder | A source whose files AiSloth keeps, starting empty or from an upload, with versions | Sources (planned) | upload, directory, bucket |
| Changes | What differs in a nook's copy of a source from what it started with: its commits since, and what isn't committed | Nooks, Sources | diff, patch (except as a download format) |
| Instructions | What AiSloth tells every agent to follow, whatever its harness: AiSloth's own (the nook, its repositories, how setups work), a workspace's, for everyone's chats there, and a person's own, for the chats they start; written to the file each harness reads its user's instructions from | Chats | system prompt, rules, custom instructions |
| Setup | Scripts that come with a nook's files and prepare it, `.agents/setup` installing what the code needs and `.agents/resume` starting its services, at the top of the files or of a repository in them; they run whenever a nook gets its files, before its agent starts. People see it as the setup of a chat's files, never where they are | Nooks | recipe, bootstrap, init script, project setup |
| Prepare | Asking a chat's agent to write, run, and commit the setup of its files; an ordinary message, which AiSloth's instructions tell agents how to answer | Chats | init, onboard, bootstrap |
| Ready copy | A copy of a nook right after a setup that took a while, which the next nooks with the same repositories and image start from and catch up | Nooks | template, image, cache, warm pool |
| Project | An optional group of nooks, chats, and sources for a team working toward one goal | Projects (planned) | space, board, workspace |
| Harness | The program that runs a coding agent, such as Claude Code or Codex; in a nook, through its ACP adapter | Chats | agent (that's what it runs), CLI, client |
| Harness profile | What AiSloth knows about one harness: its ID, name, the credentials it takes, and where it keeps its sessions and its state; its image's start script configures it | `Bagatka.Harnesses` | adapter, plugin |
| Start script | A harness image's `harness` command: it reads the same three variables for every harness and configures and runs its harness | `src/Harnesses/start` | wrapper, entrypoint |
| Harness state | What a harness writes for itself to use in later sessions, such as Claude Code's memory: kept in its workspace as a kept folder for whoever may direct the agents, a person for the chats on their own accounts or the workspace for the chats on its accounts, and synced when an agent starts and after each turn | Chats | memory (ours), context |
| Kept folder | A folder AiSloth keeps outside every nook, by name, which nooks sync a folder of theirs with: what each changed comes together, a text file both changed keeping the lines of both | Nooks | synced folder, volume, shared folder |
| Agent account | An account at an agent vendor that pays for agents' work, such as an OpenAI API key or a ChatGPT plan: a workspace's, which every member uses, or a person's own | AgentAccounts | subscription (for API keys), credential, account (alone) |
| Secret | An environment variable a workspace gives to every process in its nooks, agents included, such as `GH_TOKEN`; readable by everyone who may write in a nook | Secrets | env var (alone), credential, key |
| Endpoint | The base URL of the API an API key is for, when it isn't the vendor's own, such as OpenRouter's for OpenAI's API | AgentAccounts | base URL, provider, upstream |
| Plan | A person's subscription at a vendor that pays for agents' work, such as a ChatGPT, Claude, or Copilot plan; always personal | AgentAccounts | subscription (for API keys), seat |
| Sign-in | Proving who you are in a browser or with a code: to a host, which starts a session (Users), or at a plan's vendor, which adds the plan, finished with the address the browser returns to (AgentAccounts) | Users, AgentAccounts | OAuth flow, login, connect |
| Account kind | What an agent account is at its vendor, named so vendors can't be confused: `chatgpt-plan`, `claude-plan`, `copilot-plan`, `openai-api-key`, `anthropic-api-key` | AgentAccounts | provider, type |
| Proposal | A message in a chat from someone who may not use its account; it never reaches the agent until the account's owner sends it on, as is or edited | Chats | suggestion, draft |
| Sleep | A nook releasing its compute when nobody used it for its sleep period, two minutes by default: Asleep, its provider keeping its memory (Paused) or only its files (Stopped), and Evicted after a long sleep; any use wakes it. People see all of these as asleep | Nooks | suspend (in the product), hibernate, idle shutdown |
| Paused | A sleeping nook whose compute is released with memory and files kept; it resumes in about a second and processes continue | Nooks | hibernated, hot |
| Stopped | A sleeping nook whose compute is released with files kept; it resumes in seconds and processes start again | Nooks | archived, cold, shut down |
| Evicted | A nook asleep so long, a day by default, that its sandbox was deleted; its files are in its latest checkpoint, and it starts again from there when used | Nooks | shelved, archived, deleted |
| Offline | A nook whose daemon is away and whose provider can't be asked, such as on a machine that is off; its daemon dialing in again makes it Ready | Nooks | disconnected, unreachable, lost |
| Usage | What a nook uses of its disk, memory, and CPU, as its daemon last reported while it runs; a disk nearly full is told | Nooks | metrics, stats, quota |
| Process | A program the daemon runs in a nook until it exits or is stopped, independent of the control plane. Agents, setup scripts, and one-off commands are all processes. | Nooks | job, task, command |
| Watch | Streaming a process's output from an offset, first what was kept and then live; any number per process | Nooks | subscription, tail |
| Preview | A web server running in a nook, opened in a browser through the control plane | Nooks (planned) | port forward, tunnel |
| Daemon | `slothd`, the process in every nook that dials the control plane and runs processes for it | `src/Daemon` | agent, sidecar, runner |
| Daemon token | The secret a daemon proves its nook with; issued by Nooks, stored only as a hash | Nooks | API key, password |
| Daemon endpoint | The WebApi's HTTP/2-only gRPC endpoint that daemons and machines dial | WebApi | agent API, callback |
| Nook image | An image nooks start from: `slothd` under tini, on Ubuntu 26.04 with git and the Docker engine, alone or with one harness; a nook's image is chosen when it is created and never changes | `src/Daemon/Dockerfile` | base image, runner image |
| Disk reserve | Space a daemon holds in a file and releases when the disk fills, so output and cleanup keep working | `src/Daemon` | ballast, buffer |
| Reconciler | The Nooks job that makes providers match the records: it creates the sandboxes of new nooks and deletes those of deleted ones | Nooks | sync job, worker |
| Instruction | A message from the control plane telling a daemon what to do | Nooks, `daemon.proto` | command, request |
| Agent | A coding agent, such as Claude Code, Codex, or Amp, working inside a nook | — | bot, assistant, the daemon |
| Agent actor | An agent acting for the user who sent the current message | `Bagatka.Foundation` (planned) | bot user, service account |
| Machine | A computer a workspace adds to run its nooks on, such as a VPS or a Mac mini; its nooks still run isolated, in containers or virtual machines. To nooks, a place within the `machine` provider. | Machines | runner, worker, node, host |
| Machine mode | `sloth machine run`: the CLI keeping a machine connected and running the control plane's provider calls on its Docker Engine | `src/Cli` | agent, runner, daemon |
| Registration code | The one-time code an owner gets when adding a machine, traded for the machine's token by `sloth machine connect` | Machines | invite, pairing code |
| Cloud | The host's own place to run nooks, as people see it, whatever runs it underneath: `sloth chat --on cloud`. A host running several providers of its own, as in development, shows each by its name instead | Nooks | azure, docker (to people), hosted |
| Provider ID | Where a nook runs, as callers name it: a provider, then the place within it for a provider with several, such as `docker` or `machine:<machine ID>` | Nooks | provider name (for the whole ID), backend |
| Location | The place within a sandbox provider where a sandbox runs, such as a region or a machine | `Bagatka.Sandboxing` | zone, target |
| Sandbox | A provider's isolated machine: the technical term beneath a nook, used only in `src/Sandboxing` | `Bagatka.Sandboxing` | nook (in provider code) |
| Sandbox provider | The implementation of the provider contract for one compute backend, such as Docker or Azure Container Apps Sandboxes | `src/Sandboxing` | driver, adapter, backend |
| Sandbox key | The caller's identifier for a sandbox at a provider; AiSloth uses the nook's ID | `Bagatka.Sandboxing` | resource name |
| Snapshot | A saved copy of a sandbox's files that new sandboxes can start from | `Bagatka.Sandboxing` | image, backup |
