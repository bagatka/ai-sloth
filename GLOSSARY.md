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
| User | A person who signs in. One user can have access to many workspaces and nooks. | Users | account, customer |
| Workspace | Where people work together and what owns nooks, like a Slack workspace: a personal one, a company one | Workspaces | organization, team, tenant |
| Member | Someone given access to a workspace | Workspaces | participant, seat |
| Access level | How much a person may do with a resource: Read (see), Write (work), or Manage (decide who else has access). Each includes the ones below it. | Workspaces | role, permission |
| Resource | Something people are given access to: a workspace or a nook, later a project. Access to it reaches everything in it. | Workspaces | place, scope, space |
| Grant | One person's access level on one resource, given directly | Workspaces | membership, ACL entry |
| Invite | A one-time code that gives whoever accepts it first an access level on a resource, for 7 days | Workspaces | invitation, link |
| Guest | Someone with access to a nook but not to its workspace | Workspaces | external user |
| Nook | Where an agent works: an isolated machine with its files and processes, owned by a workspace, created by its chat; with it, the basic unit. A nook without a chat runs processes only. A provider's sandbox underneath. | Nooks | sandbox, orb, VM, container, environment, workspace |
| Chat | A conversation between people and one coding agent, in the nook the chat creates for it; one chat per nook | Chats | thread, session, conversation |
| Turn | One message in a chat and everything the agent did in reply, ending with a stop reason | Chats | step, exchange |
| Steering | A message sent during a turn going into that turn, so the agent reads it while it works | Chats | interrupt, injection |
| Stop | Ending the running turn at once; messages the agent hasn't received are cancelled | Chats | cancel (in the product), abort, interrupt |
| Model gateway | The WebApi endpoint agents call their model through; it forwards each call to their chat's agent account's endpoint with the headers that pay for it, so no nook holds a key or a plan's token | WebApi, Chats | LLM proxy, API proxy |
| Checkpoint | A nook's source files saved at one moment, such as after a turn | Nooks (planned) | snapshot, backup |
| Fork | A new nook started from a checkpoint, with the chat resumed up to that point | Nooks, Chats (planned) | clone, copy, branch |
| Source | Where some of a nook's files come from, mounted at `/work/<name>`: a repository or a folder | Sources (planned) | repo (for both kinds), mount |
| Repository | A source from a git remote such as GitHub or GitLab | Sources (planned) | repo link, git source |
| Folder | A source whose files AiSloth keeps, starting empty or from an upload, with versions | Sources (planned) | upload, directory, bucket |
| Changes | What differs in a nook's copy of a source from what it started with | Nooks, Sources (planned) | diff, patch (except as a download format) |
| Recipe | A source's setup script, safe to run again, owned by AiSloth and versioned | Sources (planned) | setup script, bootstrap, skill |
| Template | A snapshot of a nook right after its recipes ran, used to start new nooks fast | Nooks (planned) | image, cache, warm pool |
| Project | An optional group of nooks, chats, and sources for a team working toward one goal | Projects (planned) | space, board, workspace |
| Harness | The program that runs a coding agent, such as Claude Code or Codex; in a nook, through its ACP adapter | Chats | agent (that's what it runs), CLI, client |
| Harness profile | What AiSloth knows about one harness: its ID, name, and the credentials it takes, and later where it keeps state; its image's start script configures it | `Bagatka.Harnesses` | adapter, plugin |
| Start script | A harness image's `harness` command: it reads the same three variables for every harness and configures and runs its harness | `src/Harnesses/start` | wrapper, entrypoint |
| Harness state | The files a harness keeps between sessions, such as its memory and skills, saved per person and restored into their new nooks | Chats (planned) | memory (ours), context |
| Agent account | An account at an agent vendor that pays for agents' work, such as an OpenAI API key or a ChatGPT plan: a workspace's, which every member uses, or a person's own | AgentAccounts | subscription (for API keys), credential, account (alone) |
| Secret | An environment variable a workspace gives to every process in its nooks, agents included, such as `GH_TOKEN`; readable by everyone who may write in a nook | Secrets | env var (alone), credential, key |
| Endpoint | The base URL of the API an API key is for, when it isn't the vendor's own, such as OpenRouter's for OpenAI's API | AgentAccounts | base URL, provider, upstream |
| Plan | A person's subscription at a vendor that pays for agents' work, such as a ChatGPT, Claude, or Copilot plan; always personal | AgentAccounts | subscription (for API keys), seat |
| Sign-in | Adding a plan by signing in at its vendor in a browser, which AiSloth finishes with the address the browser returns to | AgentAccounts | OAuth flow, login, connect |
| Proposal | A message in a chat from someone who may not use its account; it never reaches the agent until the account's owner sends it on, as is or edited | Chats | suggestion, draft |
| Paused | A nook whose compute is released with memory and files kept; it resumes in about a second and processes continue | Nooks | hibernated, sleeping, hot |
| Stopped | A nook whose compute is released with files kept; it resumes in seconds and processes start again | Nooks | archived, cold, shut down |
| Process | A program the daemon runs in a nook until it exits or is stopped, independent of the control plane. Agents, setup scripts, and one-off commands are all processes. | Nooks | job, task, command |
| Watch | Streaming a process's output from an offset, first what was kept and then live; any number per process | Nooks | subscription, tail |
| Preview | A web server running in a nook, opened in a browser through the control plane | Nooks (planned) | port forward, tunnel |
| Daemon | `slothd`, the process in every nook that dials the control plane and runs processes for it | `src/Daemon` | agent, sidecar, runner |
| Daemon token | The secret a daemon proves its nook with; issued by Nooks, stored only as a hash | Nooks | API key, password |
| Daemon endpoint | The WebApi's HTTP/2-only gRPC endpoint that daemons and machines dial | WebApi | agent API, callback |
| Nook image | An image nooks start from: `slothd` under tini, on Ubuntu 26.04 with git, alone or with one harness; a nook's harness is chosen when it is created and never changes | `src/Daemon/Dockerfile` | base image, runner image |
| Disk reserve | Space a daemon holds in a file and releases when the disk fills, so output and cleanup keep working | `src/Daemon` | ballast, buffer |
| Reconciler | The Nooks job that makes providers match the records: it creates the sandboxes of new nooks and deletes those of deleted ones | Nooks | sync job, worker |
| Instruction | A message from the control plane telling a daemon what to do | Nooks, `daemon.proto` | command, request |
| Agent | A coding agent, such as Claude Code, Codex, or Amp, working inside a nook | — | bot, assistant, the daemon |
| Agent actor | An agent acting for the user who sent the current message | `Bagatka.Foundation` (planned) | bot user, service account |
| Machine | A computer a workspace adds to run its nooks on, such as a VPS or a Mac mini; its nooks still run isolated, in containers or virtual machines. To nooks, a place within the `machine` provider. | Machines | runner, worker, node, host |
| Machine mode | `sloth machine run`: the CLI keeping a machine connected and running the control plane's provider calls on its Docker Engine | `src/Cli` | agent, runner, daemon |
| Registration code | The one-time code an owner gets when adding a machine, traded for the machine's token by `sloth machine connect` | Machines | invite, pairing code |
| Provider ID | Where a nook runs, as callers name it: a provider, then the place within it for a provider with several, such as `docker` or `machine:<machine ID>` | Nooks | provider name (for the whole ID), backend |
| Location | The place within a sandbox provider where a sandbox runs, such as a region or a machine | `Bagatka.Sandboxing` | zone, target |
| Sandbox | A provider's isolated machine: the technical term beneath a nook, used only in `src/Sandboxing` | `Bagatka.Sandboxing` | nook (in provider code) |
| Sandbox provider | The implementation of the provider contract for one compute backend, such as Docker or Azure Container Apps Sandboxes | `src/Sandboxing` | driver, adapter, backend |
| Sandbox key | The caller's identifier for a sandbox at a provider; AiSloth uses the nook's ID | `Bagatka.Sandboxing` | resource name |
| Snapshot | A saved copy of a sandbox's files that new sandboxes can start from | `Bagatka.Sandboxing` | image, backup |
