# Nooks

Nooks are where agents work: isolated machines with their files and processes, created on the
provider a workspace member chooses. A nook and its chats are AiSloth's basic unit; a project may
group nooks, but a nook never needs one. This module tracks nooks' lifecycle, starts them fast from
templates, runs processes in them through their daemons, and checkpoints and forks them.

## Owns

- **Data:** nook records (workspace, provider, sources, status, latest disk usage), the hash of
  each nook's daemon token, the processes started in each nook, templates, and checkpoints.
- **Rules:** who may use a nook (members of its workspace), the lifecycle below, when an idle nook
  is suspended, and which providers exist.
- **Integrations:** sandbox providers (`src/Sandboxing`), registered by the host.
- **Runtime state:** each running nook's daemon connection and active watches, in the memory of
  the instance the daemon dialed.

## Does not own

- Workspaces and membership: Workspaces.
- The wire protocol and its gRPC endpoint: `src/Daemon` and the WebApi. This module sees only the
  records in its contract.
- What runs in a nook, such as agent chats: other modules start and watch processes through
  `INooksApi`.
- Sources and their recipes: Sources. A nook mounts each of its sources at `/work/<name>`.
- Projects: a project refers to nooks; nooks never refer to projects.
- People's own computers: Machines. A nook runs either on an AiSloth cloud provider or on a
  workspace's machine; this module decides which provider runs each nook in one place, and asks
  Machines for nooks placed on a machine.

## Contract

`INooksApi` in `Bagatka.AiSloth.Nooks.Contracts`: workspace members and their agents create, list,
and delete nooks, and start, watch, feed, and stop processes in them. `INookDaemonsApi` is the
daemon endpoint's side, never a public route or a tool.

## Asks

Workspaces (`GetRoleAsync`), on every call made for a user; Sources (planned), for what to mount
and how to set it up.

## Publishes

Nothing yet. `NookCreated` and `NookDeleted` come with their first consumer, usage billing.

## Reacts to

Nothing yet. Once workspaces can be deleted, `WorkspaceDeleted` deletes their nooks.

## Lifecycle

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

Schema `nooks`. Tables `nooks` (ID, workspace ID, provider, status, created at, daemon token hash;
the status has a concurrency token) and `processes` (ID, nook ID, command, arguments, started at,
exit code).

## Background work

- **Reconciler** (planned): compares records with each provider's `ListAsync`, in bounded batches,
  safely on several instances. Recorded `Creating` but missing at the provider: call `CreateAsync`
  again. Recorded `Deleting`: call `DeleteAsync` until the sandbox is gone, then remove the record.
  At the provider, not recorded, and older than a grace period: delete it. Reported failed: mark it
  `Failed`.
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

Settings, passed by the host (`PATTERNS.md`, entry 20): the daemon image, the URL daemons dial,
default resources, and the idle period before suspension.

## Decisions and constraints

- **Record first, then create.** A nook is committed before its provider is called, so every
  sandbox at a provider has a record. Reconciliation finishes what a failed call left undone.
- **Suspension is invisible.** Every operation on a paused or stopped nook resumes it first and
  waits for its daemon, so callers only notice latency.
- **Processes are detached.** A process runs until it exits or is stopped. Watches come and go, and
  a control-plane deploy only makes the daemon reconnect; nothing it started stops.
- **Output is never silently lost where it matters.** A process started with `Complete` retention
  (agent conversations) never loses output; it blocks instead when far too much waits unread.
  `Recent` processes keep a scrollback. Files are never affected by either.
- **Disk usage is reported, not enforced here.** The daemon reports how full the nook's disk is,
  and the nook record keeps the latest report; Chats decides what a nearly full disk means for a
  new message. On the Docker provider the figure is the host's disk, because Docker can't cap a
  container's disk on most setups.
- **Daemon tokens.** Each nook gets a random token when it is created. It reaches the daemon
  through the provider's environment (`SLOTHD_TOKEN`, with `SLOTHD_NOOK_ID` and
  `SLOTHD_CONTROL_PLANE_URL`), and only its hash is stored. This module verifies it, which is why
  `INookDaemonsApi` takes an anonymous actor. Anything inside the nook can read the token, so it
  grants only what that nook's daemon needs.
- **One active instance for now.** Daemon connections live in the instance they dialed. A deploy
  hands them over with `ReconnectInstruction`; several active instances are designed when capacity
  requires them.
