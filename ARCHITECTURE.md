# Architecture

AI Sloth separates durable product state from disposable compute and explicit
source-code publication.

```text
                 +-> Accounts module -> Accounts D1
                 +-> Workspaces module -> Workspaces D1
API Worker ------+
                 +-> SessionCoordinator DO
                              |
             +----------------+----------------+
             |                |                |
        Session D1       Durable R2      Disposable sandbox
                              |                |
               Git + Pi + transcript artifacts    Git + Pi modules
                                               |
                                      Ephemeral R2 backups

GitHub ---------------- source reads and explicit publication only
```

## Modules

- `src/api`: HTTP transport and route mapping. It owns no account, session,
  persistence, Git, Pi, or sandbox behavior.
- `src/web-ui`: Vite and React browser client. It owns web presentation and
  browser interaction; backend capabilities remain behind the HTTP API.
- `src/accounts`: password authentication, opaque bearer sessions, users, and
  their D1 schema.
- `src/workspaces`: collaborative workspaces, equal membership, invitation
  tokens, and their D1 schema.
- `src/github`: user-scoped GitHub App authorization, encrypted OAuth tokens,
  trusted repository discovery, repository credentials, and idempotent pull
  request creation. Connections never belong to an AI Sloth workspace.
- `src/sessions`: repository-scoped nested projects and one coordinator Durable
  Object per session. It owns project instructions, session placement, revision
  transactions, D1 metadata, durable R2 Git and Pi artifacts, cache selection,
  and publication state.
- `src/sandbox`: Cloudflare sandbox creation, directory layout, agent-process
  shutdown, backup/restore, cleanup, and container lifecycle.
- `src/image`: the container image, unprivileged agent account, pinned Pi
  runner, and installed runtime tools.
- `src/project-setup`: bounded dependency preparation policy. It currently
  installs locked npm dependencies with lifecycle scripts disabled, fingerprints
  the lockfile, and versions warm caches whenever that policy changes.
- `src/git`: exact source checkout, protected repository state, bounded local
  checkpoints, self-contained Git artifacts, restoration, and trusted publish.
- `src/pi`: bounded Pi execution, event validation, and Pi snapshot access.

Each package root contains only its public overview and high-level capability
slices. Implementation, persistence, transport policy, and tests live under
`internal/`. Each package's `index.ts` is its complete public contract;
consumers must not import package internals. The API is the transport exception:
`app.ts` is its overview while endpoint slices remain visible.

## Session state

A user prompt creates a durable session turn before agent execution begins. A
successful turn creates a revision; a failed or interrupted turn remains visible
without advancing repository state. A completed revision consists of:

```text
trusted D1 session, turn, and revision metadata
Git checkpoint artifact in durable R2
matching Pi JSONL snapshot in durable R2
AI Sloth turn transcript in durable R2
effective project instructions snapshotted in revision metadata
```

D1 advances the current revision only after all immutable R2 objects exist.
Pi JSONL is private continuation state and is never a browser contract. The
product-owned transcript is the sole durable record of what the human sees.
A failed operation does not create a visible partial revision. Controllers are
limited to twenty durable sessions. Git artifacts
contain enough objects and the recorded shallow boundary to restore the current
checkpoint without GitHub. Revisions are limited to 100, Pi state to 16 MiB,
and Git checkpoint artifacts to 64 MiB.

Durable repository state follows Git semantics: tracked files and non-ignored
files added by the platform checkpoint are durable. Ignored dependency trees,
build outputs, caches, and other ignored files are disposable and may survive
only through an ephemeral backup.

Projects form a repository-scoped tree with at most twelve levels. Workspace
members may create, move, and edit projects. Instructions are combined from the
root project to the session's immediate project, bounded to 32 KiB, and resolved
at the start of each operation. The exact effective instructions are retained
with that revision. Moving a session or editing a project therefore affects
future revisions, not completed ones.

The creator is the session's immutable controller. Workspace membership alone
does not permit another member to move, run, publish, or discard it. Only one
coordinator operation runs at a time; D1 compare-and-set conditions remain the
final protection against conflicting revisions. Prompt requests use an
idempotency key and return after the coordinator has durably accepted the turn.
The coordinator owns the bounded background execution, so a browser reload does
not own or cancel it.

## Compute and caches

Every operation owns one fresh sandbox and destroys it on every exit. Pi runs
as the unprivileged `agent` user. The working tree is agent-writable while the
Git object database is platform-owned and only readable by the agent. Platform
Git commands always address the protected Git directory explicitly.

The session coordinator assigns turn-local event sequence numbers and server
timestamps. Normalized events are committed in bounded batches to its temporary
SQLite-backed storage before an authenticated client can read them. Active
clients replay and follow that journal through fetch-streamed NDJSON. At turn
completion or failure, the bounded journal becomes one immutable R2 transcript;
D1 retains only turn status, bounds, and the object pointer. The previous
completed journal is retained temporarily until the next turn so an in-flight
reader can drain without an unbounded subscriber queue.

Each `bash`, `edit`, or `write` tool invalidates the active aggregate. When an
authenticated panel reads it after the tool completes, the Git module creates a
bounded snapshot from the immutable session base to the Git-visible working
tree. One final snapshot is ensured before checkpointing. Snapshot creation
stages into an isolated temporary index and object directory, so untracked files
and deletions are represented without changing the protected repository index. The coordinator owns only the latest patch for the current or
most recent turn, bounds it by the final patch limit, and replaces or clears it
at the next state-changing operation or object eviction. Authenticated clients
fetch this provisional,
non-durable state through the turn-scoped working-diff endpoint; tool patches are
not composed into repository state.

After Pi exits, the sandbox module stops every agent-owned process before Pi
state is read or Git state changes. A trusted Git phase then creates one local
checkpoint, its durable artifact, and a bounded authoritative patch comparing
the session's immutable base commit with the new checkpoint. The patch is stored
in R2 beside the Git artifact and loaded only through the controller-authorized
session diff endpoint. It never compares against the moving GitHub branch. Pi
never receives a GitHub credential.

Cloudflare directory backups under `/workspace/state` provide two disposable
accelerators:

- a controller- and commit-scoped warm project checkout, retained for seven
  days and bounded to three commits per project and image version;
- a revision-scoped hot sandbox backup, retained for 24 hours.

A restored cache is validated and the durable Git checkpoint is reapplied.
Missing, expired, corrupt, or mismatched caches fall back to reconstruction from
durable R2. The entire backup bucket may be deleted without losing a completed
session. SDK TTL controls restore eligibility; an R2 lifecycle rule physically
deletes backup objects.

## GitHub authority and publication

GitHub is a source and publication system, not session persistence. Starting a
session resolves the requested branch to an immutable base commit and performs
one command-scoped authenticated checkout. Continuing a completed session does
not require GitHub.

Messages never create or advance a GitHub ref. The controller explicitly calls
`POST /workspaces/:workspaceId/sessions/:sessionId/publish`. Publication
uses a fresh sandbox in which Pi and project setup never run. It restores the
exact durable checkpoint, resolves the destination from the trusted GitHub
repository ID, pushes only `ai-sloth/<session-id>` with expected-head lease
semantics, ensures a draft pull request exists, and records the published
revision. Retries reconcile a push or pull request that succeeded before D1 was
updated. An unexpected remote head is a conflict and is never overwritten. The
controller may discard a session; deletion first marks it unavailable, removes
durable Git and Pi objects, then removes D1 state. Published GitHub refs and
pull requests remain external publication records.

Repository URLs, refs, hooks, configuration, or credentials found in the
sandbox never grant authority. Hooks, global/system Git configuration, and
credential helpers are disabled during trusted operations. Credentials exist
only in the environment of the exact trusted Git command.

## Identity and lifetime

An account session is a random opaque bearer token with a seven-day absolute
lifetime. Only its SHA-256 hash is retained in Accounts D1, and each user retains
at most ten sessions. Clients own plaintext tokens and send them in the
`Authorization` header. Tokens are never placed in URLs or logs.

Workspace invitations are random opaque tokens with a seven-day absolute
lifetime. Only their SHA-256 hashes are retained, at most twenty invitations are
retained per workspace, and a workspace must retain one member.

Durable session artifacts remain until the session is deleted or a product
retention policy removes it. Ephemeral backup objects have an eight-day physical
lifecycle safety net. The deployed container count and session request rate are
bounded by Wrangler.

## Current limits

Session execution is coordinator-owned and asynchronous after durable turn
acceptance. Active event delivery uses authenticated fetch streaming rather than
WebSockets so browser bearer credentials remain in headers. Automatic project
setup currently supports only `npm ci` for repositories with a
`package-lock.json`; dependency lifecycle scripts are disabled. Git submodules,
Git LFS, pnpm, and Python environments are not prepared automatically. Session
fork, cancellation, project deletion, older-turn pagination, workspace-wide
byte quotas, email verification, and password recovery are not implemented yet.

Session rows created under the former GitHub-branch durability model are not
read by current session operations. Their remote branches and old Pi snapshots
require an explicit migration or deletion policy.
