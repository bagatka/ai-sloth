# Open product decisions

These questions materially change security, persistence, or public behavior.
The contracts can remain stable with the recommended defaults, but production
schema/API work should not silently guess different answers.

## 1. Who owns an external connection?

**Recommended first rule:** connections remain user-owned. A member starting a
new session or publishing resolves their own connection at that moment.
Completed sessions continue from AI Sloth snapshots and do not need the source
creator's connection.

Why: this matches the current OAuth model, avoids silently sharing personal
credentials with a workspace, and makes publication attribution honest.

A future workspace-owned service connection should be a separate credential
subject with explicit administrators, audit, rotation, and revocation—not a
boolean on a user connection.

## 2. What does collaboration permit?

**Recommended first rule:** every current workspace member may read events,
read changes, and submit a turn. Exactly one turn may be active per session;
concurrent submissions receive a conflict. Record the author on every turn.

Keep destructive policy separate:

- rename/move: any member is reasonable with current equal membership;
- publish: any member who independently has target access;
- discard: creator or future workspace owner/admin until explicitly broadened.

Do not retain `controllerUserId` as a general authorization shortcut.

## 3. What is an object-store code source?

“Google Storage” can mean two materially different things:

1. one versioned archive object (`bucket/key@generation`);
2. a bucket prefix treated as a directory tree.

**Recommended first implementation:** a single `.tar.zst`/`.zip`-like archive
object with an immutable generation/version. It has one atomic identity, bounded
download, deterministic restore, and straightforward conflict-safe publish.

Prefix semantics require rules for listing consistency, pagination, symlinks,
partial updates, deletes, metadata, generations per object, and atomicity. Add
that only for a concrete use case.

## 4. What files are durable?

The current answer is “Git-visible files,” which cannot be the cross-provider
contract.

**Recommended target rule:** everything under the user workspace is durable
except explicit platform-owned ephemeral mount/directories. Dependency and
build preparation must put disposable state in those locations. Snapshot
capture has hard file/count/expanded-byte/path limits.

Decide before implementing filesystem-native snapshots how to handle symlinks,
executable bits, empty directories, sparse files, and binary changes. Safe
initial support is regular files, directories, executable bit, and validated
relative symlinks; reject device nodes, sockets, FIFOs, absolute links, and path
traversal.

## 5. Is an agent fixed for a session?

**Recommended first rule:** yes. Persist `{runner, version, profile}` at session
creation. Opaque continuation state is valid only for that key.

Changing agents becomes “fork session with transcript/context export” once a
real workflow is specified. Silently feeding one agent another agent's state is
invalid; silently dropping state loses conversational behavior.

## 6. Who chooses model credentials and cost policy?

The current image selects one model and the platform injects one OpenRouter key.
Supporting multiple agents will expose this decision quickly.

**Recommended first rule:** clients choose a server-defined `agentProfile`.
Profiles own runner version, model, tools, network policy, timeout, and billing
policy. Clients cannot submit arbitrary images, commands, base URLs, or secret
environment variables.

BYOK can be added as an encrypted user/workspace credential subject later. The
agent profile selects a scoped trusted egress/credential policy; raw secrets are
not placed in the agent-visible process environment, session records, or events.

## 7. What does Git publication attribute?

The current checkpoint commits use the synthetic `AI Sloth` author even though
publication uses a user's OAuth token.

**Recommended rule:** no commit exists until explicit publication. Create one
commit per publication request, not one commit per agent turn. The first publish
applies the selected revision to the immutable source base; a later publish can
apply the delta from the last published receipt on top of the expected external
version. The request supplies a bounded summary; the Git-host adapter applies a
documented author/committer policy and records the requesting AI Sloth user.
Prefer the external identity returned by the user's connection when the provider
supports it. Never claim the agent or platform user authored a human's commit
without making that policy visible.

Decide whether publication creates:

- a branch + draft pull request (current behavior);
- a branch only;
- a patch/archive download;
- a direct update to an existing branch.

Keep direct branch updates disabled until their conflict, review, and recovery
semantics are explicit.

## 8. Is a codebase source also its only publication destination?

**Recommended rule:** no. A codebase has one initial/default source and may have
an optional default publication target, but a publication target is a separate
validated record. This permits GitHub → Bitbucket, object storage → GitHub, or
no external publication without changing revision semantics.

The UI may default source and target together for the common GitHub case.

## 9. What happens to queued/concurrent prompts?

**Recommended first rule:** do not queue user prompts behind an active turn.
Return conflict and let the user retry after seeing the latest result. This
avoids executing a prompt against state the author had not seen.

An explicit queue can later include author-visible order, cancellation, reorder,
and base-revision semantics. Infrastructure delivery of one already accepted
turn is still at-least-once and uses an internal execution lease; that is not a
user prompt queue.

## 10. What happens when a member leaves a workspace?

**Recommended rule:** access is checked against current membership on every
operation. Historical turn/publication attribution remains. Leaving immediately
removes session access. Existing completed snapshots remain workspace-owned.

A running turn may finish under the authorization captured at durable
acceptance, but any later source or publication credential use must be defined:
the safest first policy is to resolve external authority before acceptance or
re-check membership/connection immediately before the external action.

## 11. How are old sessions handled?

The current schema includes older branch-backed records and newer durable Git
bundle records.

**Recommended rule:** do not promise automatic migration until an audit proves
that each old record has all required source, transcript, Git, and Pi artifacts.
Offer export where possible, mark unsupported records read-only, and delete only
through an explicit retention/user action. Never fabricate a workspace snapshot
or agent state for missing data.

## 12. Do sessions need private ACLs?

**Recommended first rule:** no. Workspace membership is the collaboration
boundary. Add session-level ACLs only when there is a concrete need for private
or restricted sessions; otherwise they multiply authorization paths and make
catalog/event/cache behavior harder to reason about.
