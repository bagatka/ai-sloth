# Nooks

Nooks are where agents work: isolated machines with their files and processes, on a provider the
workspace may use. This module keeps each nook's sandbox matching its record, runs processes in it
through its daemon, keeps its files as checkpoints and brings a lost nook back from the latest,
starts new nooks fast from ready copies, and keeps folders outside every nook that nooks sync with.

## Owns

- **Data:** nooks (workspace, provider and place, image, status, what they start from, their kept
  paths), their copies of sources, their processes, checkpoints (bundles in object storage), ready
  copies, and kept folders (history in object storage).
- **Rules:** who may do what with a nook (Read sees it and watches its processes; operating it,
  which is starting, feeding, and stopping processes, copying files in and out, and running its
  setup, takes being the person it is reserved for when it is, otherwise Write on its workspace; deleting it
  takes Write on its workspace; the control plane may do anything), the lifecycle below, how many
  of a workspace's nooks are awake, and which providers a workspace's nooks may run on.
- **Runtime state:** each daemon's connection and the latest usage it reported, how long each nook
  stays awake, and file locks, in the memory of the active instance.

## Does not own

Who has access to a nook (Workspaces); the wire protocol (`src/Daemon`) and its endpoint (the
WebApi); what runs in a nook, such as agents (Chats); repositories and pushing (Sources); people's
computers (Machines, to this module one more provider, `machine`).

## Contract

`INooksApi`: people and the control plane create, list, and delete nooks, run and watch processes in
them, take, list, and download checkpoints, copy files in and out, export a source's changes for
pushing, see and run their setup, wake them, and sync a folder of a nook with a kept folder.
`INookDaemonsApi` is the daemon endpoint's side. Every operation wakes a sleeping nook first, and
every operation on its files puts them in place first, so callers only notice latency.

## Asks

Workspaces (access, and registering a new nook), Machines (a workspace's machines), Secrets (every
process's environment), Sources (copying repositories in, as the nook's creator).

## Reacts to

`MachineRemoved`: the machine's nooks fail for good, and their daemons are refused.

## Lifecycle

```
Starting ──daemon connects──▶ Ready ──nobody uses it──▶ Asleep ──any use──▶ Starting
   ▲                            │                         │
   └─ sandbox lost or failed ───┤                         └─ asleep for long: its sandbox is deleted,
                                └─ daemon away, provider     and it comes back from its latest checkpoint
                                   can't be asked ──▶ Offline ──daemon connects──▶ Ready
Starting ──a new sandbox fails──▶ Failed      any ──deleted──▶ Deleting ──▶ (gone)
```

One job, `Jobs/NookLifecycle.cs`, makes each sandbox match its record, every 10 seconds and at once
after a change, one piece of work per nook at a time (`NookWork`), so a slow provider holds up only
its own nooks. It creates sandboxes, from a matching ready copy when there is one; replaces lost ones,
and failed ones that had run the nook's files, such as one that can't start again as it wakes, from
the latest checkpoint; puts nooks nobody used for the sleep period (two minutes) to sleep after
keeping their changed files as a checkpoint; deletes the sandboxes of nooks asleep for the eviction
period (a day); deletes deleted nooks; and hourly the ready copies nobody used for a week and the
sandboxes no nook records, such as those a reset database left. Providers keep a sleeping nook's
memory where they can (Paused) and otherwise its files (Stopped); people see both as Asleep.

A workspace has at most 10 nooks awake (`MaxAwakePerWorkspace`). One more starting or waking first
sends the least recently used of them that nothing keeps busy to sleep; when all of them are busy, it
can't (`TooManyAwake`).

## Parts

- `NooksApi`: the contracts' features, each in `Features/`; `ReadyAsync` is the one way a feature
  reaches a nook that must have its files.
- `NookProcesses`: processes as recorded, watched, and ended; the control plane's own scripts run
  to their end.
- `NookFiles`: putting a nook's files in place before anything runs in it (its repositories, or a
  checkpoint's files), starting its setup, and the ready copy a setup that took a while earns.
- `Checkpoints`, `KeptFolders`, `ReadyCopies`: what their names say.
- `Scripts/*.sh`: every shell script the control plane runs in nooks; they own the formats of
  checkpoints and kept folders.

## Setup

Code prepares its own nooks: `.agents/setup` installs what it needs, and `.agents/resume` starts its
services, at the top of `/work` or of a folder in it. Whenever a nook gets its files, every setup
runs (30 minutes each), then every resume (5 minutes each), in one process people can watch, whose
output also goes to `/var/log/aisloth/setup.log`. A nook that woke runs only its resumes.

## Data

Schema `nooks`: `nooks`, `source_copies`, `processes`, `checkpoints` and `checkpoint_parts`,
`ready_copies`, `kept_folders`; nooks and kept folders have a concurrency token. Objects are under
`nooks/<nook>/checkpoints/<number>/` and `folders/<name>/<commit>/`.

## Configuration

`NooksSettings`: the URL daemons dial, the base image and the other images by name (one per harness),
and, with defaults, each nook's CPU and memory, the sleep and eviction periods, how full a disk is
when people are warned (85%), and how many of a workspace's nooks may be awake (10).

## Decisions and constraints

- **Record first, then create.** A nook is committed before its provider is called, so every sandbox
  has a record; the lifecycle job finishes what a failed call left undone.
- **Usage lives with the connection.** The daemon reports disk, memory, and CPU every 30 seconds;
  the latest report is kept in memory and shown while the nook runs, never stored.
- **Daemon tokens** are random, issued right before the sandbox is created, and stored only as a
  hash; anything inside the nook can read its own token, so it grants only that nook's daemon.
- **Whoever operates a nook can use what runs in it,** an agent's credentials among it, so a nook
  can be reserved for one person (`CreateNook.ReservedFor`): a chat on a personal account reserves
  its nook for that account's owner, and nobody else changes what runs in it. People invited to one
  nook alone watch it.
- **Processes are detached** and survive deploys; watches resume from the last offset relayed.
  `Complete` output retention never loses output (agents); `Recent` keeps a scrollback.
- **Moving files is processes** (`Scripts/`), recorded like any other, so people see what ran. Nooks
  are untrusted, so what a run prints is bounded here, not by a check inside the nook
  (`ScriptOutput`): a small answer is read into memory up to its own limit, such as 64 MiB for a kept
  folder; a checkpoint, download, or export goes to the caller's stream, usually a file, up to
  2 GiB. At most two large and eight small runs hold output at once; others wait.
- **Checkpoints are git.** Each place (each repository directly in `/work`, the rest of `/work`, and
  the kept paths) becomes a snapshot commit made with a separate index, so the nook's repositories
  never change, bundled with only what the previous checkpoint lacks; every 32nd checkpoint bundles
  each place it changes whole, so a restore fetches a few dozen bundles, not one per turn. Ignored
  files, tags, git's settings besides remotes, and repositories deeper than directly in `/work`
  aren't kept; checkpoints are kept until the nook is deleted.
- **Kept folders merge by lines.** A text file two nooks changed keeps the lines of both; any other
  file keeps the syncing nook's version. A kept folder holds up to 16 MiB.
- **One active instance** holds daemon connections (`ActiveInstance`); one that hands over tells its
  daemons to reconnect and ends its watches, so watchers resume on the next.
