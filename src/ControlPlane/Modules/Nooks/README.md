# Nooks

Nooks are where agents work: isolated machines with their files and processes, created on the
provider someone who works in a workspace chooses. A chat and the nook it creates are AiSloth's basic
unit, one agent per nook; a nook without a chat runs processes only. A project may group nooks, but a
nook never needs one. This module tracks nooks' lifecycle, runs processes in them through their
daemons, checkpoints their files and brings a lost nook back from its latest checkpoint, and
later starts them fast from templates.

## Owns

- **Data:** nook records (workspace, provider, harness, who created it, what it copies, its kept
  paths, status, latest disk usage), the hash of each nook's daemon token, its copies of its sources
  (each repository's name, branch, and the commit it started from), the processes started in each
  nook, and its checkpoints, whose bundles are in object storage; templates later.
- **Rules:** what each access level allows with a nook (Read sees it and watches its processes; Write
  starts, feeds, and stops processes and deletes it; the control plane's own processes, such as Chats
  running an agent, may do anything), the lifecycle below, when an idle nook is suspended, and which
  providers a workspace's nooks may run on.
- **Integrations:** sandbox providers (`src/Sandboxing`) and object storage (`src/Storage`),
  registered by the host.
- **Runtime state:** each running nook's daemon connection and active watches, in the memory of
  the instance the daemon dialed.

## Does not own

- Who has which access level to a nook: Workspaces. Creating a nook registers it there, in its
  workspace, so the workspace's people reach it; a nook can also be shared alone.
- The wire protocol and its gRPC endpoint: `src/Daemon` and the WebApi. This module sees only the
  records in its contract.
- What runs in a nook, such as agent chats: other modules start and watch processes through
  `INooksApi`.
- Repositories, GitHub, and pushing: Sources. A nook holds a copy of each of its sources at
  `/work/<name>`; Sources makes the copy and pushes the changes, with people's GitHub connections.
- Projects: a project refers to nooks; nooks never refer to projects.
- People's own computers: Machines. To this module they are one more provider, `machine`, whose
  places are the workspace's machines; Machines says which machines a workspace has.

## Contract

`INooksApi` in `Bagatka.AiSloth.Nooks.Contracts`: people with access and their agents list the
providers they can use, create (with the workspace's repositories, or from one of another nook's
checkpoints), list, and delete nooks, start, watch, feed, and stop processes in them, take and
list checkpoints, see the project's setup and its latest run, run it again, download their files as they are or at a checkpoint, copy files out of and into
them, and export a source's changes for pushing. `INookDaemonsApi` is the daemon endpoint's side,
never a public route or a tool.

```csharp
Result<CheckpointSummary> saved = await nooks.CheckpointAsync(actor, new CheckpointNook(nookId, "Add a README"), ct);
if (saved.Failed)
{
    return new Result(saved.Error); // e.g. NotReady while the nook's daemon is away
}

// A new nook with the files of checkpoint 3 of another, and its agent's sessions kept from now on.
CreateNook copy = new CreateNook(workspaceId, "docker", "claude-code", [], CopyOf: nookId, Checkpoint: 3, KeptPaths: ["/root/.claude/projects"]);
```

A provider ID names where a nook runs: a provider the deployment runs for every workspace, such as
`docker`, or one of the workspace's machines, `machine:<machine ID>`. Callers take IDs from
`ListProvidersAsync` and never parse them.

## Asks

Workspaces (`GetAccessAsync`), on every call made for a user, and `AddResourceAsync` when creating a
nook; Machines (`ListAsync`, `GetAsync`), for
the workspace's machines when listing providers and creating a nook on one; Secrets
(`ResolveAsync`), for the workspace's secrets whenever it starts a process; Sources
(`GetRepositoryAsync` when a nook is created, `ExportAsync` and `GetGitSettingsAsync` when its
sources are copied in), as the nook's creator.

## Publishes

Nothing yet. `NookCreated` and `NookDeleted` come with their first consumer, usage billing.

## Reacts to

Nothing yet. Once workspaces can be deleted, `WorkspaceDeleted` deletes their nooks.

## Lifecycle

Built so far: Creating, Running, Unreachable, Failed, and Deleting. Paused and Stopped come with
suspension. A running nook whose daemon is away and whose sandbox is gone or failed goes back to
Creating, and its new sandbox starts from its latest checkpoint; when its provider can't be asked,
such as for a machine that is offline, it is Unreachable until its daemon or its provider answers.

```
Creating ──daemon connects──▶ Running ──sandbox lost──▶ Creating (from the latest checkpoint)
                                 │
                                 └──idle──▶ Paused or Stopped (the provider decides which)
                                 ▲                           │
                                 └──────any operation────────┘
Running ──daemon away, provider can't be asked──▶ Unreachable ──daemon reconnects──▶ Running
Unreachable ──provider says the sandbox is gone──▶ Creating (from the latest checkpoint)
any ──provider reports failure──▶ Failed
any ──user deletes──▶ Deleting ──provider confirms──▶ (record removed)
```

A nook is idle when no process is running and nobody is watching. Processes never stop because a
nook is suspended: a nook with running processes is never idle.

## Data

Schema `nooks`. Tables `nooks` (ID, workspace ID, provider name and location, status, created at and
by, the nook and checkpoint it copies, its kept paths, whether its sources are in place, the setup
scripts found then and the process running them, daemon token hash; a concurrency token), `source_copies` (nook ID and name, repository, branch, the commit
it started from), `processes` (ID, nook ID, command, arguments, started at, exit code and when it came),
`checkpoints` (ID, nook ID, number, taken at, note), and `checkpoint_parts` (checkpoint, the place it
keeps, its snapshot commit, the commit its bundle builds on, the bundle's object key). Bundles are in
object storage under `nooks/<nook ID>/checkpoints/<number>/`.

## Background work

- **Reconciler** (`Jobs/NookReconciler.cs`): runs every 10 seconds, and at once after a nook is
  recorded or deleted, in bounded batches. Recorded `Creating` and missing at the provider: issue a
  daemon token and call `CreateAsync`. Recorded `Running` or `Unreachable` without a daemon
  connection here: ask the provider, and when the sandbox is gone or failed, delete what is left of
  it, end the processes that ran there with exit code -1, and create it again from the latest
  checkpoint; when the provider can't be asked, mark it `Unreachable`, logged once. Recorded `Deleting`:
  call `DeleteAsync`, then remove the record, its processes, and its checkpoints. A `Creating` nook
  reported failed at the provider, or rejected by it: mark it `Failed`. Planned: deleting sandboxes
  without a record (after a grace period), by comparing with each provider's `ListAsync`; and
  claiming nooks atomically before several instances run it.
- **Idle suspender** (planned): suspends nooks that stay idle longer than a setting.

## Setup

A project prepares its own nooks with scripts that live in its files, as Amp's do: `.agents/setup`
installs what it needs, and `.agents/resume` starts its services, such as `docker compose up -d`.
They live in `/work` or a folder directly in it, so a repository, an upload, or a project an agent
made all carry theirs, and so do checkpoints and copies. Whenever a nook gets its files, as a new
nook, a copy, or one brought back after its sandbox was lost, the files go in, then every setup runs,
then every resume: `/work`'s own before each folder's, by name, each in its own folder, with the
workspace's secrets, 30 minutes for a setup and 5 for a resume. One process runs them all, so people
see it, and its output also goes to `/var/log/aisloth/setup.log` for the agent. A failure fails the
run with the first failing script's exit code, after the others ran; the nook works either way.
Scripts must be safe to run again, because ready copies (planned) will run them again on top of an
earlier run.

## Checkpoints

A checkpoint saves the places a nook keeps: each git repository directly in `/work`, with its
history, branches, HEAD, and remotes, and its files as they are, committed or not; the rest of
`/work`; and the nook's kept paths. Each place becomes a snapshot commit, made with a separate index
so the nook's own repositories never change, and bundled into object storage with only what the
place's previous checkpoint lacks; a place that didn't change makes the same commit and no bundle.
`.gitignore` is honored, so installed dependencies and build output aren't kept.

Putting a checkpoint back fetches each place's bundles in order and checks the snapshot out: a
repository gets its history and branches back, with its uncommitted changes uncommitted again.
That happens when a nook starts from a checkpoint, its own after its sandbox was lost or another
nook's; a copy of a nook without a chosen checkpoint takes one of it first, so there is one way to
copy files between nooks. A copy leaves out the other nook's kept paths, which belong to its agent.
Downloading a checkpoint puts it back into a folder in the nook and archives that. The scripts
(`NooksApi.Checkpoints.cs`) own the format; the control plane checks what comes back from a nook
before storing it.

## Configuration

`NooksSettings`, passed by the host (`PATTERNS.md`, entry 20): the connection string, the URL
daemons dial (the WebApi's daemon endpoint as a nook reaches it), the base nook image and the image
for each harness a nook can carry, and each nook's CPU
and memory. The host also registers the object storage checkpoints are kept in. The idle period
before suspension comes with suspension.

## Decisions and constraints

- **Every process gets the workspace's secrets** (Secrets), as they are when it starts, under its
  own variables, which win on a clash. They reach the daemon and are never stored here.
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
- **Sources go in before anything runs.** The first process started in a nook, an agent's included,
  waits while its sources are copied in: each repository as a git bundle the control plane fetched
  with the creator's GitHub connection, cloned beside its folder and moved into place, with origin
  pointing at GitHub without credentials and git set to commit as the creator. Then `/work/AGENTS.md`
  tells agents each folder is its own repository with its own instructions. A nook from a checkpoint
  gets the checkpoint's files instead, and git set to commit as its creator. Then the project's
  setup starts (see Setup), and nothing waits for it here: Chats holds its agent until it ended. One
  operation on a nook's files runs at a time per nook, preparation or checkpoint; a failure is
  returned and retried by the next call.
- **Losing a sandbox loses at most what changed since the latest checkpoint.** Chats takes one after
  every turn. The nook keeps its ID, record, and access; its processes don't survive, so they end
  with exit code -1 (`ProcessExited.Lost`).
- **Moving files is processes.** Copying in, copying out, archiving, checkpoints, and bundling
  changes are shell scripts run as processes, recorded like any other, so people see what ran. Bulk input reaches them
  on a stream of its own (`daemon.proto`, `ReadInput`), whose end closes their input; their output is
  watched as usual, with `Complete` retention. What a run writes out, an archive or a bundle, lands
  on the control plane's disk first, so a run that writes more than 4 GiB is stopped and fails.
- **Watches survive reconnects.** A watch asks the daemon for output from the last offset it
  relayed, again after every reconnect, so a watcher sees each byte once.
- **One active instance for now.** Daemon connections live in the instance they dialed. A deploy
  hands them over with `ReconnectInstruction`; several active instances are designed when capacity
  requires them.

## Not built yet

- **Suspension.** No nook is Paused or Stopped yet. A nook whose daemon stays away while its sandbox
  runs still shows Running; calls to an away nook wait up to 60 seconds for it, then answer
  `NotReady`.
- **Handover.** A control-plane instance that shuts down doesn't send `ReconnectInstruction`;
  daemons notice the lost connection and reconnect with backoff, within about a second.
- **Reconciler gaps.** Sandboxes without a record aren't deleted. Nooks aren't claimed atomically, so only one instance may run the job. Nooks
  are reconciled one at a time without deadlines, so a slow call, such as the first image pull on a
  fresh host or machine, delays every other nook, and a provider that hangs blocks them. A nook on
  a machine that is offline stays Creating, and its retries log an error every pass.
- **Lost processes.** Processes a restarted daemon lost in the same sandbox never report an exit:
  watching one ends at once with exit code -1, but the process list still shows it running. Handling
  it means marking them exited on the daemon's next hello. A replaced sandbox's processes are marked.
- **Ready copies,** and resuming a suspended nook's services with its resume scripts alone.
- **What checkpoints leave out.** Ignored files; git's settings besides remotes, which a restored
  source gets again from its creator; tags; repositories deeper than directly in `/work`, and
  submodules' files. Folders whose names hold a tab or a line break fail the checkpoint, as do more
  than 100 repositories. Checkpoints are kept until the nook is deleted; none are pruned.
- **A checkpoint's bundles are fetched whole before they go in,** so restoring and downloading need
  the nook's disk to hold them twice for a moment.
- **Sources from another workspace,** and bringing new commits into a running nook.
- **Timeouts for copying in.** A copy that hangs, such as one whose daemon never reads its input,
  holds the nook's first process until its caller gives up.
