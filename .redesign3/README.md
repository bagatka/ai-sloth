# Task-first redesign

This directory is a clean-sheet design. It deliberately does not preserve the
current package graph or the abstractions in `.redesign` and `.redesign2`.

## The one-sentence model

> AI Sloth is a durable coding task: a Git working tree, one agent conversation,
> and an ordered log of human turns.

A user imports a repository, sends messages to an agent that can edit and run
that repository, reviews the cumulative changes, and either downloads them or
explicitly publishes them.

That sentence is the architecture test. A concept that is not needed to explain
it should not become a top-level system.

## Where the earlier designs went wrong

The current implementation made each successful turn a transaction over Git,
Pi, D1, R2, a Durable Object, and a disposable sandbox. `.redesign` promoted
most of those concerns into ports. `.redesign2` reduced the list to `Code`,
`Sandboxes`, `Agents`, and `Sessions`, but it still made one user workflow cross
four architectural systems and made `CodeVersion` the central product object.

Both designs retained assumptions that are not required by the user experience:

- code must be provider-neutral rather than simply Git-backed;
- a successful turn creates a new immutable product revision;
- a failed agent turn must not preserve useful file edits;
- sandboxes and agents need public platform interfaces before a second real
  implementation exists;
- project trees and inherited platform instructions are core product concepts;
- the platform should guess dependency setup and own warm-cache policy;
- portability requires Cloudflare-free contracts throughout the backend.

A local coding agent has much simpler semantics: it runs in a mutable folder.
If a test fails after the agent edited three useful files, the files remain.
Git already supplies the vocabulary for a base, a working tree, changes, and
portable delivery. AI Sloth should preserve that model instead of placing a new
versioning model above it.

## The complete product model

```text
Team
  └─ Task
       ├─ repository       mutable current code, immutable imported base
       ├─ conversation     private native agent continuation state
       └─ turns            attributed prompts and normalized events
```

There are only three product nouns:

- **Team**: people who may access the same tasks. Every account starts with a
  personal team.
- **Task**: one ongoing coding effort in one repository with one selected agent
  profile.
- **Turn**: one attributed user message and the resulting activity.

A successful save after a turn advances the task's current working tree. It
is not a separate `Revision`, `CodeVersion`, `WorkspaceSnapshot`, or
`Checkpoint` product. Storage may use immutable generations internally for safe
replacement, but clients do not coordinate them.

A turn can fail after changing files. If the runtime can safely capture those
files, the task advances and the failed turn records that its changes were
saved. If capture fails, the previous task state remains authoritative and the
failure says that new changes could not be saved. AI Sloth never silently
pretends partial work was persisted.

## The user workflow

```text
                  ┌──────────────────────────────────────┐
GitHub or upload ─►              Task                    │
                  │                                      │
message ─────────►│ run agent in repository              │
                  │ keep edits, conversation, and log    │
message ─────────►│ run agent again                      │
                  │                                      │
                  └──────┬───────────────────┬───────────┘
                         │                   │
                    patch/archive       explicit GitHub
                      download             publication
```

The application surface is correspondingly small:

```text
create a task
send a message
read/watch the task
review its changes
export it
publish it to GitHub
remove it
```

[`model.ts`](model.ts) sketches this surface. It is intentionally a `Tasks`
API, not a global platform SDK.

## Git is the common substrate

The first product supports Git repositories. That is a useful constraint, not a
leaky GitHub dependency.

Git provides the small stable protocol that the previous designs tried to
invent:

```text
immutable input       commit
current files         working tree
change calculation    diff
safe concurrency      expected commit
portable transfer     bundle / patch / archive
publication           commit + push
```

GitHub is one way to bring a Git repository in and one optional place to publish
it. It is not the owner of task state. An enterprise repository can enter
without giving AI Sloth access to the enterprise host.

An uploaded ordinary directory can be normalized into a private Git repository
at import time if that becomes necessary. Non-Git filesystem semantics, object
store prefixes, Git LFS, and submodules should not be generalized prematurely;
they receive explicit support only for a real use case.

The durable file rule is exactly Git's rule: tracked files and non-ignored
untracked files survive; ignored files are disposable unless deliberately
force-added. The UI and CLI must report ignored files that will be omitted before
an environment is destroyed. This is an explicit Git-product limit, not a
provider-neutral promise to preserve every possible filesystem object.

### Protected task repository

Inside a machine, code and control state are separated:

```text
/work/project        agent-writable working tree
/work/control.git    platform-owned Git directory
/work/agent-state    native agent state, not part of exported code
/work/events         temporary normalized event log
/work/cache          disposable dependencies and build caches
```

The agent may read normal repository metadata needed by coding tools and works
on a disposable copy of its continuation state. It cannot write canonical
protected refs, publication configuration, credentials, or the previously saved
task image. Trusted Git commands address the protected repository directly and
run only after all agent-owned processes have stopped.

At rest, a **task image** is the private implementation format containing the
protected repository, agent continuation state, and enough metadata to verify
both. It is one logical value with a versioned manifest and digest. It is never
returned directly to a browser. The task-image module alone owns whether that
value is physically one archive or several objects committed by one manifest.

This is analogous to a filesystem implementation hiding blocks behind a file:
callers use a task, not Git bundles, R2 keys, Pi JSONL, or backup IDs.

## Only two backend systems

```text
HTTP/UI
   │
   ▼
┌─────────────────────────────────────────────────────────┐
│ Tasks                                                   │
│ authorization, task/turn state, events, export/publish │
└──────────────────────────┬──────────────────────────────┘
                           │ runTaskTurn(taskImage, message)
                           ▼
┌─────────────────────────────────────────────────────────┐
│ Runtime                                                 │
│ restore, isolate, run Pi, stop, verify, save, destroy   │
└─────────────────────────────────────────────────────────┘
```

Identity and team membership are a small supporting product area. GitHub and
HTTP are edge integrations. D1, R2, Durable Objects, Cloudflare Sandbox, Git,
and Pi are implementation mechanisms, not peer systems in a platform diagram.

### Tasks

`Tasks` is the only coding-product boundary used by HTTP handlers. It owns:

- team authorization;
- task creation and deletion;
- exactly one active turn per task;
- actor-scoped idempotency;
- turn attribution and status;
- normalized event replay and live following;
- the pointer to the current verified task image;
- cumulative change review against the imported base;
- archive and patch export;
- explicit publication and its audit receipt.

Internally it can have focused files for SQL, event storage, exports, and GitHub.
Those are modules hiding knowledge, not globally exported service interfaces.

### Runtime

The sole deep infrastructure boundary is the runtime module. Its central
operation is conceptually:

```ts
runTaskTurn(currentTaskImage, agentProfile, message, emit)
  -> { nextTaskImage, agentOutcome }
```

The same module contains explicit `importFromGitHub`, `importUpload`,
`exportArchive`, and `publishToGitHub` machine workflows. These are concrete
use-case functions, not a generic machine API. Provider API calls and token
refresh stay in `github/`; only the exact trusted Git command receives a
short-lived credential.

The runtime owns the whole dangerous lifecycle:

1. create one isolated machine;
2. restore and verify the current task image;
3. prepare the agent-visible working tree;
4. run the configured agent and normalize its events;
5. stop every agent-owned process;
6. checkpoint Git-visible edits even when the agent failed, when safe;
7. capture compatible agent continuation state;
8. build and verify the replacement task image;
9. destroy the machine on every exit.

The caller cannot run arbitrary privileged commands, manipulate native Pi
state, save half an image, forget cleanup, or accidentally receive a source
credential. This is a deep boundary. A generic `Sandbox.run`, filesystem API,
artifact store, queue, snapshot service, and agent runner would expose more
complexity rather than hide it.

There is one Pi implementation and one Cloudflare implementation. Use a direct
function and concrete types. Extract a smaller driver only when a second real
runtime or agent demonstrates what actually varies.

## Turns are serialized, not distributed workflows

One task has one coordinator. A second user message while a turn is active gets
`409 Conflict`; it is not silently queued against unseen code.

The coordinator performs this state machine:

```text
idle
  │ accept prompt durably
  ▼
running
  │ runtime returns a verified replacement image
  ▼
saving
  │ atomically replace task head and seal events
  ▼
idle
```

On agent failure, `saving` is still attempted so useful edits survive. On
machine loss or an unverifiable image, the old head remains. There are no
automatic agent retries because repeating a non-idempotent coding turn can
produce different or duplicate edits.

Sequence numbers are assigned before events are delivered. The live journal is
bounded. On completion its normalized events become durable task history; Pi's
native state remains private continuation data, never a UI contract.

The coordinator may be a Durable Object because that is the concrete deployment
primitive. It does not need `TurnQueue`, `ExecutionLease`, `EventJournal`, or
`SessionRepository` interfaces around itself.

## Import and delivery are explicit use cases

Do not create generic `Source`, `Target`, `Connector`, or `Provider` registries.
Expose concrete actions and share private mechanics only when they are actually
the same.

### Personal GitHub

```text
createFromGitHub(repositoryId, branch)
publishToGitHub(taskId, repositoryId, baseBranch, message)
```

The GitHub integration resolves the branch to an immutable commit. A trusted,
command-scoped credential imports that exact commit. Publication restores the
current task, creates a clean publication commit, pushes with an expected-head
check, and creates or recovers a draft pull request.

Internal per-turn checkpoint commits are never pushed as user history. The
publication commit has an explicit message and documented author/committer
policy.

### Enterprise or credential-free use

The default enterprise workflow needs no SSH server and no enterprise GitHub
credential in AI Sloth:

```text
sloth task create .          upload the current clean Git tree over HTTPS
sloth task diff <task>       review the cumulative patch
sloth task apply <task>      verify the base and apply the patch locally
sloth task download <task>   download a code-only archive
```

The CLI requires or explicitly records the local base commit/tree. `apply`
refuses a mismatched base unless the user chooses a three-way application. The
user reviews and pushes with their normal company tools.

This is smaller and safer than inbound SSH, and it works through ordinary
corporate egress. Add SSH transfer only if a concrete environment cannot use
authenticated HTTPS upload/download.

### Credentials are never agent tools

The normal agent process receives no GitHub token, SSH key, cloud credential, or
publication capability. Import and publication are trusted server operations
requested by an authorized human.

Do not add a checkbox that injects a user's SSH key or `gh` token into the coding
agent. That changes the product from “edits code” to “can act with a person's
external authority.” If a future power-user mode genuinely requires that, make
it a separate, visibly credentialed runtime profile with narrowly scoped,
short-lived credentials and its own audit and egress policy.

### Network and code confidentiality

Process isolation does not prevent exfiltration when a process may read source
and reach the network. Model calls intentionally disclose prompt/context to the
configured model provider, and package scripts or test programs can disclose
more if arbitrary egress is allowed.

An agent profile is therefore server-defined and fixes the image, model,
toolset, timeout, resource bounds, and egress policy. The default profile allows
only the model proxy and explicitly supported package endpoints; company
deployments may use a stricter allowlist or no package egress. Model credentials
stay in a trusted proxy and are not environment variables visible to the agent.
The UI must identify the model provider and network policy before confidential
code is uploaded. No architecture can make a personal deployment an approved
place for employer source; that remains an organization policy and deployment
decision.

## Repository instructions and build tools

The repository is the source of project knowledge. Agents read normal files such
as `AGENTS.md`, build manifests, and test configuration. A task may have one
small pinned instruction string, but there is no platform project tree and no
instruction inheritance hierarchy.

The runtime image supplies trusted toolchains. The agent runs `npm`, `.NET`,
Python, compilers, and tests itself through its normal shell tool. The platform
does not guess a package manager or run automatic `npm ci` before the agent.
That removes setup policy, setup failure states, lockfile cache keys, and a
surprising source of side effects.

Dependencies and build caches live in disposable space where practical. The
first implementation may simply rebuild them on every turn. Add a runtime-local
cache only after measurements show that it matters; deleting every cache must
never lose task code or conversation state.

## Collaboration without a permission framework

- every account has a personal team;
- a task belongs to exactly one team;
- every current team member may read it and send the next turn;
- every turn records its author;
- only one turn runs at a time;
- publication uses the requesting member's current connection and records them;
- deletion is limited to the creator initially;
- membership is checked on every operation;
- there are no per-task ACLs or generic roles until a real policy requires them.

This supports friends and small company deployments without making identity a
framework. A future company SSO implementation only needs to produce the same
user and team membership records.

## Concrete Cloudflare deployment

Keep Cloudflare for the first rewrite. Moving vendors would replace known
working isolation with new operational and security work, while doing nothing by
itself to simplify the product model.

Use fewer concrete resources:

```text
one Worker              HTTP, authentication, composition
one D1 database         users, teams, connections, tasks, turns, publications
one R2 bucket           immutable task images and code-only exports
one TaskRoom DO/task    serialization and bounded live events
Cloudflare Sandbox      untrusted execution, hidden entirely by runtime/run.ts
```

Cloudflare types are allowed in the concrete store, coordinator, and runtime.
Do not build provider-neutral wrappers around D1, R2, or Durable Objects. The
task-image format, task API, and SQL data model are the portability boundaries
that matter.

Cloudflare Sandbox backups are optional runtime accelerators, not task state.
Leave them out initially. If measured latency later justifies them, all backup
selection, validation, expiry, and deletion stays inside `runtime/run.ts` and a
cache miss follows the same authoritative restore path.

A conventional Bun service with SQLite, local object files, and a hardened
container/microVM runtime would be operationally simpler for a trusted
single-tenant installation. It should be a separate deployment when somebody
actually needs it. Ordinary rootful Docker is not an adequate untrusted
multi-tenant security boundary, so replacing Cloudflare merely to make the
diagram prettier is not a safe simplification.

## Suggested source tree

Use one backend package. TypeScript package boundaries and `index.ts` re-export
files are not architecture.

```text
src/backend/
  worker.ts                 composition root and HTTP routes
  schema.sql                the one metadata schema

  identity/
    identity.ts             accounts, login, teams, invitations
    store.ts

  tasks/
    tasks.ts                complete application surface and policy
    room.ts                 one TaskRoom Durable Object
    store.ts                task/turn/publication SQL only
    image.ts                task-image format, validation, and limits
    run.ts                  restore -> agent -> save -> destroy
    events.ts               one normalized event model
    export.ts               patch/archive generation

  github/
    github.ts               OAuth, repository listing, trusted import/publish

  web/                      browser application
```

`tasks/run.ts` may directly use small private Pi and Git files if that keeps it
readable. Split those files when they hide a coherent protocol, not to create a
package catalogue.

The dependency direction is one way:

```text
worker -> identity
worker -> tasks -> task store
                -> task image
                -> task runtime (Cloudflare + Pi + Git)
                -> GitHub integration for explicit edge operations
```

No module imports HTTP response types except `worker.ts`. No generic dependency
injection container or binder functions exist. `worker.ts` constructs concrete
objects once.

## What is intentionally removed

```text
Codebase entity
Project tree and inherited instructions
public CodeVersion / Revision model
public Sandboxes and Agents systems
provider/connector registries
WorkspaceSnapshots and ArtifactStore ports
generic repositories, queues, journals, and execution leases
automatic npm preparation
warm-project and hot-session caches
live aggregate diff recomputation after every tool
successful-turn-only file persistence
controller ownership checks on every session operation
multiple D1 databases and workspace packages
forwarding binders and service middleware
```

Final cumulative changes remain available after a turn. Individual tool events
may include convenient patches, but the first rewrite does not promise an
authoritative whole-task diff while the agent is still mutating files.

## Non-negotiable invariants

The smaller model does not weaken the hard parts:

1. A mutable source branch is resolved to one immutable base before agent work.
2. Source and publication credentials are never visible to the agent.
3. Every untrusted process is stopped before trusted Git and image creation.
4. Every machine is destroyed on success, failure, timeout, and cancellation.
5. A task head changes only to a complete, bounded, digest-verified image.
6. A failed agent turn preserves safe edits when capture succeeds and states
   clearly when it does not.
7. One task runs at most one turn; idempotent retries cannot duplicate a turn.
8. Events are bounded and sequenced before clients observe them.
9. Publication is explicit, attributed, idempotent, and expected-head protected.
10. Prompts, source contents, credentials, and model output do not enter logs.
11. Cache loss cannot lose task state.
12. All input, output, time, file-count, byte, event, and turn growth is bounded.

## Clean-slate build order

Do not migrate the old abstractions one by one. Build one new vertical path next
to the MVP:

1. **Upload to download:** create a task from a clean local Git tree, run one Pi
   turn in a fresh sandbox, save the task image, show the final diff, and
   download/apply it locally.
2. **Continuation:** restore the same task image, continue Pi state, preserve
   edits on agent failure, and enforce one active turn.
3. **Durable events:** add replay/follow and browser reload independence.
4. **GitHub import:** resolve an exact commit and import with command-scoped
   credentials.
5. **GitHub publication:** create one clean commit and draft PR only on an
   explicit request.
6. **Teams:** add member reads, attributed turns, and requester-owned
   publication credentials.

Start with a new schema and no compatibility columns. Keep old sessions
read-only until they can be exported or explicitly deleted; do not fabricate a
migration between incompatible state models.

Before adding projects, another source host, another agent, a cache, a local
runtime, queued prompts, private tasks, or SSO, require one concrete user and one
end-to-end test that needs it.

## Definition of success

A new contributor should be able to answer these questions after opening
`tasks/tasks.ts` and `tasks/run.ts`:

- What does the product store? A task.
- What happens on a message? Restore, run, stop, save, replace.
- Where can code leave? Download or explicit publication.
- Where are credentials? Trusted import/publication code, never the agent.
- What happens on failure? Save safe edits if possible; otherwise retain the old
  task and say so.
- What is Cloudflare doing? Hosting the API, coordinator, blobs, and isolated
  machine—not defining the product model.

If answering a normal product question requires tracing D1, R2, Durable Object,
GitHub, Git, Pi, and sandbox types through separate public packages, the rewrite
has failed.
