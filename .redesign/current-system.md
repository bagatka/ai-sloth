# Current backend assessment

Scope reviewed: all non-frontend implementation under `src/`, public contracts,
D1 schemas, Cloudflare configuration, image runner, deployment scripts, and the
backend test inventory. Generated dependencies, local Wrangler state, secrets,
and the web UI were intentionally excluded.

This is not a rewrite recommendation. It identifies which current boundaries
carry real policy and which merely expose the implementation above them.

## Current execution path

A new session currently does all of the following through the
`SessionCoordinator` Durable Object:

1. Resolve a user-owned GitHub App connection and expose its access token as
   `GitHubRepositoryAccess`.
2. Resolve a branch to a Git commit.
3. Reserve a GitHub-shaped session and turn in D1.
4. Create or restore a Cloudflare Sandbox, possibly using an R2-backed Sandbox
   backup.
5. Clone the GitHub repository into a separate Git work tree/object directory.
6. Make the tree writable, run npm preparation, and run the Pi-specific image
   runner.
7. Translate Pi events into product events and journal them in Durable Object
   storage.
8. Stop agent processes, stage every Git-visible file, synthesize an AI Sloth
   commit, create a Git bundle and diff, and read the Pi JSONL state.
9. Put transcript, diff, Git bundle, and Pi state into R2.
10. Advance D1 with compare-and-set semantics and optionally create another
    Cloudflare Sandbox backup.

Continuation restores the Git bundle and Pi state. Publication restores the Git
bundle in another sandbox, pushes a fixed GitHub branch, and creates/reuses a
GitHub pull request.

The behavior is careful, but the session layer knows nearly every concrete
mechanism in the system.

## Findings

### 1. The session module is the integration point and the implementation point

`src/sessions/internal/session-run.ts` imports Git, GitHub, Pi, project setup,
and Cloudflare Sandbox operations. `src/sessions/internal/session.ts` also owns
GitHub access and pull-request publication. `src/sessions/coordinator.ts` adds
Durable Object locking, HTTP event streaming, Cloudflare bindings, journal
retention, live working diffs, resource construction, and background execution.

`SessionResources` in `src/sessions/internal/contract.ts` is effectively a
vendor service-locator bag:

- `SandboxBindings["Sandbox"]`
- `D1Database`
- two `R2Bucket`s
- `GitHubOperations`
- Cloudflare/image configuration flags

Changing source provider, compute provider, agent, metadata store, artifact
store, event transport, or publication behavior therefore changes the session
core. This is the main change-amplification problem.

**Target:** `SessionService` and `TurnExecutor` own orchestration and policy, but
depend only on consumer-owned contracts for source import, snapshots, agent
execution, event journaling, persistence, queueing, and explicit publication.

### 2. `GitHubOperations` combines unrelated knowledge

`src/github/github.ts` puts all of these behind one interface:

- OAuth connection lifecycle;
- repository discovery and pagination;
- credential acquisition;
- branch resolution;
- repository metadata;
- pull-request creation.

The return type `GitHubRepositoryAccess` includes a raw `accessToken` and clone
URL. As a result, GitHub authentication details cross into sessions and Git.
The session persists `githubUserId` so later work remains tied to the creator's
GitHub identity.

**Target:** keep GitHub OAuth, token refresh, API shapes, and repository browsing
inside a GitHub integration module. Let that module separately implement the
narrow `CodeSource` and `CodePublisher` contracts. Neither contract returns a
credential. Do not generalize OAuth setup until another integration proves a
common flow.

### 3. The `git` package is GitHub-specific and is also the durable state model

`src/git/internal/checkout.ts` validates only `https://github.com/...git` URLs.
`gitCredentialEnvironment` hard-codes GitHub's HTTPS host. The checkpoint module
requires a session UUID/revision, creates a synthetic commit with AI Sloth
identity, writes AI Sloth refs, and publishes only `ai-sloth/<session-id>`.

This package currently owns three different concerns:

1. Git transport and credential-safe checkout/push;
2. session durability through Git bundles and synthetic commits;
3. user-facing aggregate diffs.

That makes Git mandatory even when the source is an object-store archive and
makes every successful turn look like a commit whether the user requested one
or not.

**Target:**

- Git checkout belongs inside GitHub/Bitbucket source adapters or a private Git
  transport used by them.
- Durable filesystem state belongs to `WorkspaceSnapshots` and must not expose
  Git semantics.
- Publication-time commits/pushes belong to Git-host publishers and happen only
  after an explicit user request.
- Change rendering belongs to snapshot/change-set behavior and may still use a
  unified textual diff where appropriate.

The current Git bundle implementation can temporarily back
`WorkspaceSnapshots`; the abstraction is not complete until callers no longer
store or branch on commit SHAs.

### 4. The sandbox package exports the vendor rather than hiding it

`SandboxInstance` in `src/sandbox/internal/sandbox-instance.ts` contains the raw
Cloudflare `ISandbox`. Git, Pi, and project setup call Cloudflare SDK methods and
use Cloudflare process types directly. `src/sandbox/index.ts` also re-exports
`ContainerProxy` and the Durable Object class.

The package centralizes paths and cleanup but does not form a portable compute
boundary. Backup IDs and R2's two-object physical layout also reach session
storage/orchestration.

**Target:** expose an `ExecutionEnvironment` with bounded command execution,
streaming output, filesystem transfer, agent-process termination, and explicit
destruction. A Cloudflare adapter owns `ISandbox`, Durable Objects, backup
layout, egress interception, and SDK workarounds. Cache restore is an internal
optimization; authoritative workspace restore always comes from a durable
snapshot.

### 5. Pi has a good adapter idea, but Pi details still define the contract

There are related event models in three places:

- `RunnerEvent` in `src/image/runner.ts`;
- `PiEvent` in `src/pi/internal/pi.ts`;
- `SessionEvent` in `src/sessions/events.ts`.

`src/pi` validates and bounds the process protocol well, but its public API is
Pi-named, consumes a Cloudflare sandbox, exposes process result fields, and
returns a Pi JSONL snapshot. The image hard-codes the default model/provider,
runner path, agent user, and OpenRouter environment convention.

**Target:** one platform-owned `AgentEvent` protocol and an `AgentRunner` port.
A Pi adapter owns native Pi events, JSONL, runner files, process handling, model
selection, and state compatibility. Model secrets remain in a trusted egress or
credential adapter and are not process-visible merely because Pi runs in the
sandbox. Other adapters may use a CLI, SDK, or remote service and return a
differently formatted opaque state artifact. Session code should not inspect any
agent's state.

### 6. Workspace collaboration stops at the session boundary

The API first verifies workspace membership, but every session operation then
passes the current user as `controllerUserId`. `SessionStore` rejects reads,
messages, event access, moves, publication, and deletion unless that user is the
immutable creator. Cache keys and persisted source credentials are also
controller-scoped. If that creator is removed from the workspace, remaining
members still cannot take over those workspace sessions.

That is ownership, not collaboration. Merely removing the check would be unsafe
because prompts and external writes currently lack independent actor policy and
publication relies on the creator's GitHub identity.

**Target:** sessions belong to a workspace and store `createdByUserId` for audit,
not blanket authorization. Store `authorUserId` on every turn and
`requestedByUserId` on every publication. Apply named policies at the
`SessionService` boundary. Continue from platform snapshots without source
credentials; import/publish operations resolve the requesting actor's current
connection explicitly.

### 7. The persisted domain is provider-shaped

The session schema and in-memory records repeat:

- `github_repository_id`
- `github_user_id`
- `repository_url`
- `base_ref`
- commit SHAs
- publication branch and pull-request fields

Projects and routes are keyed by GitHub repository ID. There is no stable
product-owned codebase record. The initial migration also retains tables from
an older branch-as-durability model alongside the newer durable session tables,
which makes the source of truth harder to see.

**Target:** introduce `codebases`, with an opaque source descriptor owned by the
connector. Core session tables refer to `codebase_id`, a resolved source
version, workspace snapshot refs, agent key/state refs, author IDs, and generic
publication receipts. Provider-specific connection and target data stays in
provider-owned storage.

### 8. Persistence responsibilities are concentrated rather than hidden

`src/sessions/internal/session-store.ts` is about 1,370 lines. It combines:

- session/turn/revision state transitions;
- authorization checks;
- D1 SQL;
- R2 object naming and I/O;
- artifact validation and cleanup;
- hot/project cache indexing and eviction;
- publication state;
- project instruction lookup;
- abandoned-attempt recovery.

A large module is not automatically bad, but this one changes for unrelated
reasons. Its public methods also exchange Git-, Pi-, Sandbox-, D1-, and R2-owned
types.

**Target:** keep transactional session state in one deep `SessionRepository`,
but move immutable byte storage to `ArtifactStore`, workspace representation to
`WorkspaceSnapshots`, event bytes to `EventJournal`, and disposable cache policy
to the compute adapter. Do not split each SQL statement into a repository class;
split by ownership of knowledge.

### 9. Event durability and HTTP delivery are fused

`TurnEventLog` has valuable sequencing, batching, bounding, and normalization
behavior. However, it is built directly on Durable Object storage and returns
an HTTP `Response`. `SessionCoordinator.fetch` parses internal trusted headers,
performs authorization-related lookup, repairs interrupted state, enforces live
follower count, and chooses between live DO batches and archived R2 content.

**Target:** retain append-before-delivery and bounded replay in `EventJournal`.
Expose `AsyncIterable<SessionEvent>` (or equivalent) to the application. Let the
HTTP adapter decide NDJSON/SSE, headers, and disconnect handling. Cloudflare DO
storage can remain the first implementation.

### 10. Several package/API layers are forwarding layers

`bind*Operations` plus `bind*client`/`bind*` modules and the API's `withX`
middleware mostly construct or forward another object without hiding policy.
`bindSessions`, for example, forwards each call to a Durable Object stub, while
five separate API middleware files bind services per request.

Not every adapter is harmful, but these layers make navigation harder without
reducing caller knowledge.

**Target:** construct the application once at the Worker composition root. Keep
an adapter only where it translates a real boundary: HTTP to application,
Cloudflare to execution/storage/queue, or provider protocol to source/publisher.
Delete forwarding layers as each vertical slice moves.

### 11. Project setup is policy but currently shares the raw sandbox

`src/project-setup` contains a clear bounded npm policy, including lockfile
fingerprinting and disabled lifecycle scripts. It is a real concern, but its API
is tied to `SandboxInstance` and its cache marker is placed in the Git directory.

**Target:** make setup operate on `ExecutionEnvironment` and put its versioned
markers/caches in platform-owned ephemeral space. Keep one direct preparer until
a second preparation strategy exists; do not build a plugin framework now.

### 12. Deployment composition is Cloudflare-specific by design

Wrangler configuration and Nushell deployment scripts correctly own D1, R2,
Durable Objects, containers, rate limits, secrets, and lifecycle setup. This
code does not need to become provider-neutral. The problem is that these binding
types currently flow beyond the composition/adapters into domain contracts.

**Target:** keep `src/api/wrangler.jsonc` and Cloudflare operations as one
production composition. Portability means another composition can be written,
not that deployment configuration itself becomes generic.

## Module disposition

| Current module | Keep, split, or replace | Target ownership |
| --- | --- | --- |
| `accounts` | Keep mostly as-is | Identity application module; D1 construction moves to composition |
| `workspaces` | Keep; add explicit session policies later | Workspace membership and invitations |
| `github` | Split public surface | GitHub connection/catalog UI plus private source and publisher adapters |
| `git` | Make private mechanism | Git-host import/publish; temporary snapshot implementation only |
| `sandbox` | Replace public contract | Cloudflare implementation of `ExecutionBackend` |
| `pi` + `image` | Adapt | Pi implementation of `AgentRunner` |
| `project-setup` | Keep direct policy | Workspace preparation over `ExecutionEnvironment` |
| `sessions` | Refocus and split by ownership | `SessionService`, `TurnExecutor`, repository, journal, contracts |
| session `catalog` | Move toward codebases | Projects/instructions keyed by `codebaseId` |
| `api` | Thin transport + composition | Authentication, validation, response/stream mapping |

## Existing behavior to lock with tests before extraction

1. Mutable source refs resolve once to an immutable source version.
2. No source/publish credential is visible to the agent process.
3. Agent processes cannot survive into trusted snapshot/publication phases.
4. Turn request idempotency returns the same turn.
5. Only one revision can advance from an expected previous revision.
6. All artifacts are durable and validated before revision visibility.
7. Failed/interrupted turns retain bounded transcripts but no revision.
8. Event sequence is durable before a client can observe it.
9. Environment destruction runs on success, failure, timeout, and cancellation.
10. External publication is explicit, idempotent, and conflict-protected.
11. Caches can be absent/corrupt/deleted without losing completed work.
12. Limits remain enforced at untrusted/process/storage boundaries.
