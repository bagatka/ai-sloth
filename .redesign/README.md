# Platform redesign notes

This directory is a design workspace. It does not change the running MVP.

The goal is not to hide the current implementation behind generic wrappers. The
goal is to identify the few stable concepts the product actually needs, put each
piece of hard logic behind one deep boundary, and keep GitHub, Git, Pi, and
Cloudflare as replaceable implementations at the edge.

## Product direction captured here

1. A successful agent turn creates an **AI Sloth revision**, not a Git commit.
2. A revision contains a durable workspace snapshot, agent continuation state,
   and a product-owned transcript.
3. Importing code and publishing changes are separate capabilities. A session
   may start from GitHub, Bitbucket, an object-store object, an upload, or an
   empty tree. Publication is optional and always explicit.
4. Agents can modify the workspace, but cannot receive source-host or
   publication credentials and cannot publish by themselves.
5. A session belongs to a workspace. Every prompt and external write is
   attributed to a user; session access is not hard-coded to its creator.
6. Cloudflare remains a good first deployment, but Cloudflare types do not
   belong in the domain or provider-neutral contracts.
7. We will add abstractions only at demonstrated variation points: code source,
   publication, agent runtime, disposable execution, durable workspace
   snapshots, event delivery, and session persistence/scheduling.

## Do not create one “source code storage” abstraction

That name currently hides three different contracts with different authority and
lifetimes:

- **`CodeSource`** imports an immutable initial tree from an external location;
- **`WorkspaceSnapshots`** durably stores AI Sloth's own revisions;
- **`CodePublisher`** explicitly exports one revision to an external target.

GitHub happens to participate in the first and third, while Git bundles currently
implement the second. GCS may participate in either external role. Keeping these
separate prevents Git branch/commit/PR semantics from contaminating session
persistence and prevents external credentials from becoming agent capabilities.

## The central model

```text
Workspace
  └─ Codebase                     stable product identity
       ├─ source                  GitHub / Bitbucket / object / upload / empty
       ├─ projects                organization + inherited instructions
       └─ sessions
            ├─ agent              selected and fixed for the session
            ├─ immutable base     resolved source + base workspace snapshot
            ├─ turns              user-attributed prompts, including failures
            ├─ revisions          successful turns only
            │    ├─ workspace snapshot
            │    ├─ opaque agent state
            │    ├─ transcript
            │    └─ change summary
            └─ publications       explicit, user-attributed external writes
```

A **codebase** replaces the GitHub repository ID as the product-level grouping
key. It is a workspace-owned record that points at a provider-owned code
location. Projects and sessions refer to `codebaseId`, never to a GitHub ID.

A **source** is where the initial file tree comes from. A source selector may be
mutable (for example, branch `main`), so it is resolved to an immutable provider
version (for example, a commit SHA or object generation) before the initial turn
is durably accepted. Retries then materialize that exact version.

A **workspace snapshot** is AI Sloth's authoritative durable filesystem state.
It is independent of Git and independent of a running sandbox. This removes the
need to create a synthetic commit after every turn.

A **publication** exports one completed revision to an external target. A Git
publisher may create a commit, branch, or pull request, but that is publication
behavior—not session persistence. An object-store publisher may write a new
versioned archive instead.

## Target dependency direction

```text
HTTP / CLI / future clients
            |
       SessionService             application policy and authorization
            |
   +--------+---------+-------------------+
   |                  |                   |
SessionRepository  EventJournal       TurnQueue
   |                                      |
   +-------------- TurnExecutor ----------+
                         |
          +--------------+-------------------------------+
          |              |              |                |
      CodeSource   ExecutionBackend   AgentRunner   WorkspaceSnapshots
                                                           |
                                                      ArtifactStore

Explicit PublicationService ---> CodePublisher
```

The interfaces are owned by the consuming application layer. GitHub does not
publish a large `GitHubOperations` interface and ask the session module to
understand it. Instead, the session module asks for the narrow `CodeSource` and
`CodePublisher` capabilities it needs; a GitHub adapter implements those
contracts while keeping OAuth, token refresh, API pagination, clone URLs, and
credentials private.

## Deep modules

### `SessionService`

The client-facing application boundary. It authenticates/authorizes the actor,
accepts turns idempotently, exposes sessions/events/changes, and requests
publication. HTTP is only an adapter; this interface must not return `Response`
or accept Cloudflare bindings.

### `CodeSource`

Resolves a provider-specific selector to an immutable version and materializes
that exact version into an empty execution environment. It owns provider
credentials and provider-specific validation. It never returns a clone token or
credential.

This is deliberately not a universal `Provider` interface. Connection setup and
repository/bucket browsing remain integration-specific until two providers show
a genuinely common product contract.

### `WorkspaceSnapshots`

Owns the durable workspace format, inclusion policy, integrity checks, limits,
restore, and change calculation. It can inspect a bounded provisional change
set without creating a revision, while final capture occurs only after agent
processes stop. Callers see opaque snapshot references. The implementation may
initially wrap today's Git bundle code, but its contract must not mention
commits, branches, or Git.

The long-term snapshot policy should capture all user work while excluding only
platform-owned ephemeral/cache locations. Dependency preparation should place
large disposable state outside the durable workspace instead of relying on a
source-provider ignore file.

### `ExecutionBackend`

Owns disposable compute creation, command execution, filesystem transfer,
network profile, agent identity, process termination, and destruction. The
lease is explicitly destroyed on every exit. Cloudflare Sandbox is one adapter;
a local Docker or another cloud adapter should not affect agents or sessions.

Backup/restore is an optimization internal to an execution/snapshot adapter. It
is not part of the correctness contract and should not appear in session logic.

### `AgentRunner`

Runs a selected agent in an execution environment, translates its native event
protocol to the small product protocol, and returns opaque continuation state.
Pi JSONL, Pi event names, runner paths, model selection, and process exit details
stay inside the Pi adapter. Raw model secrets stay outside the agent process in
the execution backend's trusted egress/credential path.

An agent selection includes a state-format version and is fixed for a session.
That makes continuation honest. Switching agents should initially be an
explicit fork/new session rather than an implicit best-effort conversion.

### `SessionRepository`

Owns the session/turn/revision state machine, idempotency, execution leases, and
compare-and-set completion. It is a domain persistence boundary, not a generic
SQL wrapper. A successful completion becomes visible only after all referenced
artifacts are durable. A failed turn retains its transcript and does not advance
the revision.

### `EventJournal`

Owns sequence assignment, durable append-before-delivery, bounded replay,
sealing, and live following. It yields an async stream; the API adapter turns
that stream into NDJSON/SSE/WebSocket as appropriate. It does not return an HTTP
`Response`.

### `CodePublisher`

Performs one explicit external write with optimistic conflict protection. It
uses the requesting actor's authorized connection (or an explicitly configured
workspace connection), restores the exact requested revision, and returns an
opaque receipt. A publisher may have provider-specific setup and target types at
the API edge; the session core stores only a validated target handle and a
receipt.

## Failure vocabulary

Core failures describe the user's operation, not the current vendor. Prefer
`source_not_connected`, `source_not_found`, `source_unavailable`,
`publication_forbidden`, and `publication_conflict` over `github_*`. Keep
`timeout`, `limit_exceeded`, `conflict`, `invalid_state`, and `internal_error`
distinct where caller action differs. Provider adapters translate GitHub,
Bitbucket, GCS, Cloudflare, or agent-native failures once at their boundary and
preserve the private cause for diagnostics without returning credentials or
sensitive payloads.

Do not force every port to return one enormous global error union. Each deep
module owns the small expected failure set for its operation; `SessionService`
translates those into the stable product/API failure contract.

## Collaboration baseline

The smallest useful collaborative policy is:

- every workspace member can read a session and its completed/live events;
- every workspace member can submit the next turn;
- exactly one turn executes per session at a time;
- each turn stores `authorUserId` and each publication stores
  `requestedByUserId`;
- idempotency is scoped to actor + operation + containing workspace/session; a
  key reused with a different payload is a conflict;
- publication requires the requesting actor to have access to the target;
- credentials used to import or publish are never inherited from the session
  creator;
- destructive actions use a separately named policy (initially creator or
  workspace owner), not a blanket `controllerUserId` check on every operation.

If private sessions or fine-grained roles become a real requirement, add a
session ACL then. Do not build a role/permission framework preemptively.

## Invariants worth preserving from the MVP

The implementation is messy at its seams, but several decisions are strong and
should survive the redesign:

- source credentials are not exposed to the agent;
- the initial mutable branch is resolved to an immutable base;
- publication is explicit and uses expected-head protection;
- turn acceptance, event ordering, artifact durability, and revision advance
  are distinct steps;
- failed turns remain visible but do not create revisions;
- every operation has bounded time/output/artifact/event limits;
- agent processes are stopped before trusted snapshot/publication work;
- a fresh disposable environment is destroyed on every exit;
- warm/hot backups are disposable accelerators, never authoritative state;
- product events are normalized instead of exposing the Pi session file to the
  client, and private reasoning is omitted from the product transcript;
- operational logs use opaque IDs and timings rather than prompts, source URLs,
  model output, or credentials.

## What not to build

- no `Provider` or `Manager` interface with dozens of optional methods;
- no lowest-common-denominator abstraction that pretends branches, pull
  requests, object generations, and uploaded archives are the same;
- no generic `Database`, `Repository<T>`, or D1-shaped wrapper;
- no plugin framework or dependency-injection container;
- no generic identity-provider interface until SSO/external login is a real
  requirement; the stable session boundary only needs an authenticated `Actor`;
- no raw credential, vendor SDK type, `D1Database`, `R2Bucket`, Durable Object,
  or HTTP `Response` in core contracts;
- no compatibility flags for hypothetical agents/providers;
- no automatic commit or external write as part of turn completion.

## Likely ownership in production

The single [`platform.ts`](platform.ts) file is useful for seeing the whole
system, but it should not become a permanent `common` package imported by
everything. Place each contract beside its consumer as implementation starts:

```text
src/codebases/                 Codebase + project/instruction policy
src/sessions/                  SessionService, state machine, executor, ports
src/workspace-state/           WorkspaceSnapshots + snapshot format
src/agents/pi/                 Pi implementation of AgentRunner
src/integrations/github/       GitHub connection UI, CodeSource, CodePublisher
src/execution/cloudflare/      ExecutionBackend + Cloudflare composition
src/api/                       HTTP adapter and production wiring
```
  
A private Git transport may be shared by the GitHub and Bitbucket integrations.
It is not a domain package and should not define session revisions. Likewise,
R2/D1/Durable Object code belongs in Cloudflare implementations of contracts,
not in a generic infrastructure facade.

## Files

- [`current-system.md`](current-system.md) records the concrete coupling found
  in the backend implementation and maps current modules to target ownership.
- [`platform.ts`](platform.ts) is a compact TypeScript sketch of the strategic
  contracts. It is a design artifact, not a package to wire into production all
  at once.
- [`migration.md`](migration.md) gives an incremental extraction order that
  keeps the MVP running.
- [`open-decisions.md`](open-decisions.md) records product questions that change
  persisted or security semantics, with recommended defaults.
