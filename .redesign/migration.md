# Incremental migration

Do not replace the MVP in one pass and do not create every interface as an empty
package before it has a caller. Extract one vertical seam at a time, keep the
current Cloudflare/GitHub/Pi implementations behind it, and preserve observable
behavior with tests.

## Ground rules

- Keep Cloudflare as the first production composition.
- Keep existing HTTP endpoints working until an additive API is usable.
- Define each port beside its consumer, not in a generic `common` package.
- Move behavior behind a port in the same change that introduces the port.
- Add a second implementation only after the first adapter passes a contract
  test; do not add provider registries/plugins first.
- Prefer new canonical tables and an explicit data migration over adding more
  nullable provider columns to `durable_sessions`.
- Caches remain optional at every stage. Test reconstruction with an empty cache.

## Phase 0 — lock the invariants

Before structural changes, add a small backend integration suite around the
current session boundary. Existing unit tests cover command construction and
individual stores, but the most important cross-module behavior needs direct
protection.

Protect at least:

1. exact source version resolution;
2. no credential in the agent command/environment/files;
3. stop-agent-before-checkpoint ordering;
4. idempotent acceptance of start and continued turns;
5. compare-and-set revision advancement;
6. all-artifacts-before-visible completion;
7. failed turn retains transcript and does not advance;
8. destruction on each failure phase;
9. cache miss/corruption fallback;
10. explicit, idempotent, expected-head publication.

Use fakes at the future seams rather than mocking every D1/Sandbox method. This
suite becomes the safety net for the extractions below.

## Phase 1 — establish product vocabulary and composition

Introduce provider-neutral IDs/types in the session application:

- `CodebaseId`
- `SourceSelection`
- `ResolvedSource`
- `AgentKey`
- `WorkspaceSnapshotRef`
- `PublicationTarget` / `PublicationReceipt`

Construct long-lived application services once in the Worker composition root.
Remove the `withAccounts`, `withGitHub`, `withWorkspaces`, `withSessionCatalog`,
and `withSessions` forwarding middleware only when their replacement is wired;
do not churn HTTP handlers first.

No runtime behavior changes in this phase.

## Phase 2 — extract `AgentRunner` around Pi

This is the smallest high-value seam because `src/pi` already behaves like an
adapter.

1. Put `AgentRunner`, `AgentEvent`, and `AgentKey` in the session/turn executor's
   contract module.
2. Implement `PiAgentRunner` using the current `src/pi` and image runner.
3. Move Pi-to-product event normalization entirely into that adapter. The
   session event log must not import `PiEvent`.
4. Return opaque agent state bytes tagged by the stored `AgentKey`; remove
   `PiSessionSnapshot` from session storage APIs.
5. Configure Pi's model/tools through a trusted server-side agent profile rather
   than `pi-settings.json` as global product policy.
6. Add one fake runner contract test, then add a second minimal runner or a
   no-op/test runner to prove the seam before generalizing configuration.

Keep the session's selected `AgentKey` immutable. A state-version mismatch fails
clearly; it does not silently start a fresh conversation.

## Phase 3 — extract `ExecutionEnvironment` from Cloudflare Sandbox

Replace `SandboxInstance.sandbox: ISandbox` with the consumer-owned environment
contract.

1. Implement `CloudflareExecutionEnvironment` inside `src/sandbox`.
2. Move process output collection/disposal, path layout, `runuser`, agent process
   termination, SDK capability cleanup, egress interception, and destruction
   into the adapter.
3. Convert Pi, Git, and project setup one at a time to the new command/files
   surface.
4. Make destruction idempotent and keep it in one `finally` owner in the turn
   executor.
5. Add a local Docker implementation only if it is immediately used for tests or
   development; otherwise a focused fake is enough.

Do not expose backup operations on the generic environment. Initially leave the
Cloudflare cache wrapper adjacent to the Cloudflare adapter, then pull cache
selection out of session orchestration once authoritative snapshot restore is
available.

## Phase 4 — make workspace snapshots the source of durability

Introduce `WorkspaceSnapshots` and first implement it with the current Git
bundle/checkpoint code. This temporary adapter may still synthesize commits
internally, but session records and orchestration must see only snapshot and
change-set refs.

Then replace it with a filesystem-native format:

- immutable and self-contained;
- content length and digest verified;
- bounded files, bytes, paths, and archive expansion;
- safe against absolute paths, traversal, links/device nodes, and decompression
  bombs;
- deterministic enough for integrity and deduplication if deduplication is
  actually needed;
- capture does not mutate the working tree;
- restore requires no source-provider access.

Recommended filesystem policy: the durable workspace contains user source and
agent-created work. Platform dependency/build caches live in a separate
platform-owned ephemeral root. Do not use `.gitignore` as the universal
persistence policy; it does not exist for all source types and is controlled by
the imported project.

After this phase:

- a successful turn does not require `git add` or `git commit`;
- revision identity is the AI Sloth revision number + snapshot digest;
- continuation restores a workspace snapshot and opaque agent state;
- aggregate changes compare the immutable session base snapshot to a revision;
- live changes use a bounded provisional inspection, invalidated around any
  agent tool marked as potentially workspace-mutating;
- Git is no longer required in a session sourced from an archive/object.

## Phase 5 — introduce `Codebase` and `CodeSource`

Add a workspace-owned codebase record. A minimal schema shape is:

```text
codebases
  id
  workspace_id
  name
  source_connector
  source_resource
  source_revision             nullable provider selector
  default_publication_target  nullable opaque validated handle
  created_at / updated_at
```

Change new projects and sessions to refer to `codebase_id`. Keep an additive
compatibility lookup from the old GitHub repository route while the UI/API is
migrated.

Implement `GitHubCodeSource` by moving these behaviors out of session code:

- actor connection lookup and token refresh;
- repository lookup/access validation;
- mutable branch resolution;
- exact commit materialization;
- command-scoped credential injection and redaction.

The returned `ResolvedSource` contains only provider/resource/version/display
identity. It contains no clone URL or token. Resolve and persist it before the
initial turn is acknowledged; materialization retries therefore use the same
version even if a branch moves. Persist the base workspace snapshot once exact
materialization succeeds.

Only after the GitHub adapter is stable, add the next real source:

- **Bitbucket Git:** validates the Git-host adapter split from GitHub API/OAuth.
- **Object storage:** first specify whether a source is one archive object or a
  bucket prefix. Prefer a versioned archive object for the first implementation;
  its generation/version ID naturally supplies an immutable source version and
  avoids ambiguous directory listing semantics.

Do not make object storage pretend to have branches or pull requests.

## Phase 6 — separate explicit publication

Move publication out of `session.ts` into a `PublicationService` and
`CodePublisher` adapter.

For the GitHub implementation:

1. restore the requested completed workspace snapshot in a fresh environment;
2. materialize/verify the immutable source base as needed;
3. compute/apply the revision without involving the agent;
4. create a commit only now, with explicit summary/attribution policy;
5. push with expected external version protection;
6. create/recover the pull request;
7. persist an idempotent generic receipt and the requesting user ID.

The session transaction does not call publication. A turn can succeed while a
publication fails. Repeating the same publication request returns/reconciles the
same external result.

Keep provider-specific target setup outside the session core. GitHub can offer
branch/base/PR settings; an object-store target can offer bucket/key/generation
settings. Once validated, the core receives an opaque target handle.

Also retain a provider-independent export path (change set and/or workspace
archive) so the user can take work without granting an external publisher.

## Phase 7 — enable collaborative sessions

Make authorization a deliberate application policy rather than a
`controller_user_id` predicate embedded throughout SQL.

Add:

- `created_by_user_id` on sessions for audit;
- `author_user_id` on every turn;
- `requested_by_user_id` on every publication;
- actor/operation-scoped idempotency plus a request hash so changed payloads
  cannot reuse a key silently;
- workspace-scoped lookups in the session service;
- one active-turn constraint/CAS per session.

Recommended first policy:

- workspace members may read and submit turns;
- the latest accepted turn wins no special privilege—another turn is rejected
  while one is active, rather than implicitly queued;
- source import and publication use the requesting actor's current connection;
- session continuation uses platform snapshots and requires no provider access;
- deletion remains limited to creator/workspace owner until a broader destructive
  policy is explicitly chosen.

Update `user_message` events to contain the author ID. Remove
`controllerUserId` from cache keys; use codebase/resolved-source/profile keys and
ensure no cached credential or user-specific secret is present.

## Phase 8 — separate scheduling and event transport from Durable Objects

Retain the Durable Object implementation first, but put it behind:

- `TurnQueue.enqueue(turnId)` with at-least-once semantics;
- `SessionRepository.claimTurn(...)` with an expiring execution lease;
- `EventJournal` with append/seal/read operations.

Acceptance writes the turn and a dispatch marker in the same metadata
transaction. A bounded outbox dispatcher enqueues it idempotently and clears the
marker; a failed request or queue call therefore cannot strand an accepted turn.
An executor claims the turn, so duplicate delivery or a restarted worker cannot
run it concurrently. Completion uses expected-revision CAS. A lease that expires
leaves a recoverable interrupted/queued turn according to explicit policy.

Adapt `EventJournal.read` to HTTP NDJSON in `src/api`; remove `Response`, trusted
internal HTTP headers, and route parsing from the coordinator. Cloudflare DO
storage can still provide ordering/following. A later queue/Postgres/other cloud
composition should require adapters, not domain changes.

## Phase 9 — migrate and delete old paths

Once new sessions use the canonical model:

1. stop writing legacy GitHub-shaped session columns/tables;
2. migrate only records with a proven mapping and intact artifacts;
3. provide an explicit export/delete policy for unmigratable sessions;
4. verify references before deleting old R2 objects;
5. remove old branch-as-durability tables, duplicate migration history, binders,
   and Git/Pi/Cloudflare types from core package exports;
6. update `ARCHITECTURE.md` and the public API documentation from the final
   behavior, not from this proposal.

## Definition of a completed redesign

The redesign is complete when a test can run the same session application with:

- a fake or local execution backend instead of Cloudflare;
- a non-Git source without installing/using Git for persistence;
- a second agent without session code importing its native types;
- two workspace users contributing attributed turns;
- no publication at all, or an explicit publication adapter;
- caches deleted between every turn;

and the session state machine, event contract, revision semantics, and API-level
behavior remain unchanged.
