# Nooks

Nooks are where agents work: isolated machines with their files and processes, created on the
provider someone who works in a workspace chooses. A nook and its chats are AiSloth's basic unit; a project may
group nooks, but a nook never needs one. This module tracks nooks' lifecycle, starts them fast from
templates, runs processes in them through their daemons, and checkpoints and forks them.

## Owns

- **Data:** nook records (workspace, provider, harness, sources, status, latest disk usage), the hash of
  each nook's daemon token, the processes started in each nook, templates, and checkpoints.
- **Rules:** what each access level allows with a nook (Read sees it and watches its processes; Write
  starts, feeds, and stops processes and deletes it; the control plane's own processes, such as Chats
  running an agent, may do anything), the lifecycle below, when an idle nook is suspended, and which
  providers a workspace's nooks may run on.
- **Integrations:** sandbox providers (`src/Sandboxing`), registered by the host.
- **Runtime state:** each running nook's daemon connection and active watches, in the memory of
  the instance the daemon dialed.

## Does not own

- Who has which access level to a nook: Workspaces. Creating a nook registers it there, in its
  workspace, so the workspace's people reach it; a nook can also be shared alone.
- The wire protocol and its gRPC endpoint: `src/Daemon` and the WebApi. This module sees only the
  records in its contract.
- What runs in a nook, such as agent chats: other modules start and watch processes through
  `INooksApi`.
- Sources and their recipes: Sources. A nook mounts each of its sources at `/work/<name>`.
- Projects: a project refers to nooks; nooks never refer to projects.
- People's own computers: Machines. To this module they are one more provider, `machine`, whose
  places are the workspace's machines; Machines says which machines a workspace has.

## Contract

`INooksApi` in `Bagatka.AiSloth.Nooks.Contracts`: people with access and their agents list the
providers they can use, create, list, and delete nooks, and start, watch, feed, and stop processes
in them. `INookDaemonsApi` is the daemon endpoint's side, never a public route or a tool.

A provider ID names where a nook runs: a provider the deployment runs for every workspace, such as
`docker`, or one of the workspace's machines, `machine:<machine ID>`. Callers take IDs from
`ListProvidersAsync` and never parse them.

## Asks

Workspaces (`GetAccessAsync`), on every call made for a user, and `AddResourceAsync` when creating a
nook; Machines (`ListAsync`, `GetAsync`), for
the workspace's machines when listing providers and creating a nook on one; Sources (planned), for
what to mount and how to set it up.

## Publishes

Nothing yet. `NookCreated` and `NookDeleted` come with their first consumer, usage billing.

## Reacts to

Nothing yet. Once workspaces can be deleted, `WorkspaceDeleted` deletes their nooks.

## Lifecycle

Built so far: Creating, Running, Failed, and Deleting. Paused, Stopped, and Unreachable come with
suspension and with noticing daemons that stay away.

```
Creating ──daemon connects──▶ Running ──idle──▶ Paused or Stopped (the provider decides which)
                                 ▲                           │
                                 └──────any operation────────┘
Running ──daemon doesn't reconnect in time──▶ Unreachable ──reconnects──▶ Running
any ──provider reports failure──▶ Failed
any ──user deletes──▶ Deleting ──provider confirms──▶ (record removed)
```

A nook is idle when no process is running and nobody is watching. Processes never stop because a
nook is suspended: a nook with running processes is never idle.

## Data

Schema `nooks`. Tables `nooks` (ID, workspace ID, provider name and location, status, created at,
daemon token hash; the status has a concurrency token) and `processes` (ID, nook ID, command, arguments, started at,
exit code).

## Background work

- **Reconciler** (`Jobs/NookReconciler.cs`): runs every 10 seconds, and at once after a nook is
  recorded or deleted, in bounded batches. Recorded `Creating` and missing at the provider: issue a
  daemon token and call `CreateAsync`. Recorded `Deleting`: call `DeleteAsync`, then remove the record
  and its processes. Reported failed at the provider, or rejected by it: mark it `Failed`. Planned:
  deleting sandboxes without a record (after a grace period) and noticing running nooks whose sandbox
  failed, by comparing with each provider's `ListAsync`; and claiming nooks atomically before several
  instances run it.
- **Idle suspender** (planned): suspends nooks that stay idle longer than a setting.

## Templates

A template is a snapshot of a nook taken right after its sources' recipes ran. It is labelled with
the recipes' versions, the base image, and a fingerprint of the sources' lockfiles. A new nook starts
from the template with the matching label, or the nearest one; then each source moves to its target
revision and the recipes run again. Because recipes are safe to run again, a matching template makes
that step nearly instant and an older one makes it incremental. A missing label triggers a template
build in the background.

A template only makes starts faster; it never changes the result. If a recipe fails, the nook still
starts and reports the failure.

## Checkpoints and forks (planned)

A checkpoint saves a nook's source files, with untracked files and honoring `.gitignore`, as a
commit in each source's git repository, stored in object storage so it outlives the nook. A fork is
a new nook from a checkpoint: from its template, then the checkpoint's files, then the recipes.
"Fork now" takes a checkpoint first, so there is one mechanism. Chats records a checkpoint after
every turn, which is what makes forking from an older message possible.

## Configuration

`NooksSettings`, passed by the host (`PATTERNS.md`, entry 20): the connection string, the URL
daemons dial (the WebApi's daemon endpoint as a nook reaches it), the base nook image and the image
for each harness a nook can carry, and each nook's CPU
and memory. The idle period before suspension comes with suspension.

## Decisions and constraints

- **Every provider is the same to a nook.** A nook stores a provider name and an optional location,
  the place within the provider, such as a machine; the reconciler passes the location in the
  sandbox spec. The one difference between providers is who may use them: the deployment's serve
  every workspace, and a machine serves only the workspace that added it.
- **Record first, then create.** A nook is committed before its provider is called, so every
  sandbox at a provider has a record. Reconciliation finishes what a failed call left undone.
- **Suspension is invisible.** Every operation on a paused or stopped nook resumes it first and
  waits for its daemon, so callers only notice latency.
- **A nook carries at most one harness,** chosen when it is created: its image is the base image
  with that harness installed, so hosts pull only the harnesses their nooks use. Chats in the nook
  run that harness; switching harness means a new nook.
- **A process's environment is never stored.** Variables passed to a process may hold secrets, such
  as an agent's token: they reach the daemon and nothing keeps them.
- **Processes are detached.** A process runs until it exits or is stopped. Watches come and go, and
  a control-plane deploy only makes the daemon reconnect; nothing it started stops.
- **Output is never silently lost where it matters.** A process started with `Complete` retention
  (agent conversations) never loses output; it blocks instead when far too much waits unread.
  `Recent` processes keep a scrollback. Files are never affected by either.
- **Disk usage is reported, not enforced here.** The daemon reports how full the nook's disk is,
  and the nook record keeps the latest report; Chats decides what a nearly full disk means for a
  new message. On the Docker provider the figure is the host's disk, because Docker can't cap a
  container's disk on most setups.
- **Daemon tokens.** The reconciler issues a random token right before it asks the provider for the
  sandbox; a retried attempt issues a new one, because the old one never reached a daemon. It reaches
  the daemon through the provider's environment (`SLOTHD_TOKEN`, with `SLOTHD_NOOK_ID` and
  `SLOTHD_CONTROL_PLANE_URL`), and only its SHA-256 hash is stored. This module verifies it, which is why
  `INookDaemonsApi` takes an anonymous actor. Anything inside the nook can read the token, so it
  grants only what that nook's daemon needs.
- **Watches survive reconnects.** A watch asks the daemon for output from the last offset it
  relayed, again after every reconnect, so a watcher sees each byte once.
- **One active instance for now.** Daemon connections live in the instance they dialed. A deploy
  hands them over with `ReconnectInstruction`; several active instances are designed when capacity
  requires them.

## Not built yet

- **Suspension.** No nook is Paused or Stopped yet, and none becomes Unreachable: a nook whose daemon
  stays away still shows Running, and calls wait up to 60 seconds for it, then answer `NotReady`.
- **Handover.** A control-plane instance that shuts down doesn't send `ReconnectInstruction`;
  daemons notice the lost connection and reconnect with backoff, within about a second.
- **Reconciler gaps.** Sandboxes without a record aren't deleted, and a running nook whose sandbox
  fails stays Running. Nooks aren't claimed atomically, so only one instance may run the job. Nooks
  are reconciled one at a time without deadlines, so a slow call, such as the first image pull on a
  fresh host or machine, delays every other nook, and a provider that hangs blocks them. A nook on
  a machine that is offline stays Creating, and its retries log an error every pass.
- **Lost processes.** Processes a restarted daemon lost never report an exit: watching one ends at
  once with exit code -1, but the process list still shows it running. Handling it means marking
  them exited on the daemon's next hello.
- **Disk usage** is stored and returned, but nothing acts on it yet; Chats will ask for
  confirmation at 90%.
- **Checkpoints, forks, and templates.**
