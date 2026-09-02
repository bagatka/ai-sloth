You’re right. I overdesigned it.

I replaced one coupled implementation with a catalogue of ports. That is cleaner engineering plumbing, but it is **not a simple platform model**. The existing `platform.ts` exposes byte storage, commands, filesystems, execution leases, snapshots, agent-native concerns, event journals, repositories, and queues at the same conceptual level.   

The original goal was already correct: identify a few stable concepts and hide the hard logic behind deep boundaries. 

## The entire platform should be four systems

Three product systems and one infrastructure primitive:

```text
Code
Sandboxes
Agents
Sessions
```

That is enough.

```text
external source
      │
      ▼
 Code.import
      │
      ▼
CodeVersion ────────► Sessions
      │                  │
      │                  ▼
      │               Agents.run
      │                  │
      │                  ▼
      └──────────── new CodeVersion
                         │
                         ▼
              Code.download / Code.publish
```

The central type is:

```ts
type CodeVersionId = string;
```

It means:

> An immutable, durable version of all the user’s files.

It is not a Git commit. It is not a Cloudflare backup. It is not an R2 object. It is not an archive path. Implementations can use any of those internally.

## The complete top-level interface

```ts
interface Platform {
  code: Code;
  sandboxes: Sandboxes;
  agents: Agents;
  sessions: Sessions;
}
```

### 1. Code

```ts
interface Code {
  import(
    user: UserId,
    source: SourceId,
  ): Promise<CodeVersionId>;

  compare(
    user: UserId,
    before: CodeVersionId,
    after: CodeVersionId,
  ): Promise<string>;

  download(
    user: UserId,
    version: CodeVersionId,
  ): Promise<ReadableStream<Uint8Array>>;

  publish(input: {
    user: UserId;
    version: CodeVersionId;
    target: TargetId;
    message: string;
  }): Promise<Publication>;
}
```

In plain English:

```text
Bring code in.
Compare two versions.
Download a version.
Publish a version.
```

Inside `Code` can live:

* source adapters;
* provider credentials;
* immutable snapshot storage;
* archive validation;
* change calculation;
* Git publishing;
* object-storage publishing;
* publication conflict handling.

Those are not top-level platform systems.

Import and publication still remain completely separate operations with separate credentials and failure semantics. The important correction is that this separation does not require two more public architectural systems. One deep `Code` module can expose both verbs while using separate source and publisher components internally. 

GitHub-specific configuration can remain GitHub-specific:

```ts
const sourceId = await github.addSource({
  repository: "acme/payments",
  branch: "main",
});
```

GCS configuration can remain GCS-specific:

```ts
const sourceId = await gcs.addSource({
  bucket: "customer-code",
  object: "payments.tar.zst",
});
```

Both return a `SourceId`. There is no generic provider configuration structure containing meaningless `resource`, `revision`, and `connector` strings.

The same applies to `TargetId`.

### 2. Sandboxes

```ts
interface Sandboxes {
  open(version: CodeVersionId): Promise<Sandbox>;
}

interface Sandbox {
  run(
    program: string,
    args?: readonly string[],
  ): Promise<{
    exitCode: number;
    stdout: string;
    stderr: string;
  }>;

  save(): Promise<CodeVersionId>;

  close(): Promise<void>;
}
```

In plain English:

```text
Open a computer with this code.
Run something.
Save its files as a new version.
Close the computer.
```

That is the entire sandbox abstraction.

Cloudflare Sandbox implements it today. Docker might implement it later. Neither Cloudflare bindings, Durable Objects, R2 backup IDs, container proxies, process SDK types, filesystem layout, network interception, nor cleanup workarounds cross the boundary.

Unlike the earlier `ExecutionEnvironment`, this does not try to define a generic operating-system SDK. Start with the operations you actually need.

`Sandbox` is deliberately lower-level than the other systems because it is a reusable infrastructure primitive. It could be used independently for tests, builds, command execution, project analysis, or agents.

### 3. Agents

```ts
interface Agents {
  run(input: {
    agent: AgentId;
    version: CodeVersionId;
    message: string;
    state?: AgentStateId;
    emit?: (event: AgentEvent) => void | Promise<void>;
  }): Promise<{
    version: CodeVersionId;
    reply: string;
    state?: AgentStateId;
  }>;
}
```

In plain English:

```text
Give an agent some code and a message.
Get back new code and a reply.
```

That is what an agent does in your platform.

The caller does not:

* create a sandbox;
* restore a snapshot;
* start a process;
* understand Pi JSONL;
* stop agent processes;
* save continuation bytes;
* capture files;
* destroy the sandbox.

`Agents.run` owns all of that.

A Pi implementation may use `Sandboxes`. A remote agent implementation might call an external API and never create a sandbox. A deterministic code transformer might run directly. The interface does not care.

The event protocol should also be tiny:

```ts
type AgentEvent = {
  type: "message" | "activity";
  text: string;
};
```

Do not define tools, patches, process exits, native message IDs, mutation flags, or Pi event types in the platform interface. Those are adapter details.

### 4. Sessions

```ts
interface Sessions {
  create(input: {
    user: UserId;
    workspace: WorkspaceId;
    version: CodeVersionId;
    agent: AgentId;
  }): Promise<SessionId>;

  send(input: {
    user: UserId;
    session: SessionId;
    message: string;
  }): Promise<TurnId>;

  get(
    user: UserId,
    session: SessionId,
  ): Promise<Session>;

  watch(
    user: UserId,
    turn: TurnId,
    after?: number,
  ): AsyncIterable<SessionEvent>;
}
```

In plain English:

```text
Create a session.
Send a message.
Read the session.
Watch a turn.
```

`Sessions` owns:

* workspace membership checks;
* multiple participating users;
* author attribution;
* one active turn;
* turn history;
* current code version;
* agent continuation state;
* revision numbering;
* durable events;
* idempotency;
* scheduling;
* retries;
* leases;
* failure recovery.

The current backend session path coordinates roughly ten concrete steps and consequently knows nearly every implementation mechanism.  The answer is not to promote those ten steps into ten public interfaces. The answer is to bury them inside `Sessions.send`.

For example:

```ts
await sessions.send({
  user: alice,
  session,
  message: "Add retry handling to payment capture.",
});
```

Internally, that may mean:

```text
authorize Alice
accept turn
enqueue work
load current version and agent state
call Agents.run
persist events
create the next revision
advance current version atomically
record Alice as author
```

But none of that is the caller’s concern.

## What should disappear from the top-level architecture

These names should not be in the one-file platform model:

```text
TurnExecutor
WorkspaceSnapshots
ArtifactStore
AgentRunner
EventJournal
SessionRepository
TurnQueue
CodeSource
CodePublisher
ExecutionBackend
ExecutionEnvironment
```

Some of them may still exist as **private interfaces inside their owning module**.

For example:

```text
Code/
  internal SourceReader
  internal VersionStore
  internal Publisher

Agents/
  internal PiDriver
  internal AgentStateStore

Sessions/
  internal SessionStore
  internal EventLog
  internal Scheduler

Sandboxes/
  internal CloudflareSandbox
```

They should be introduced only when they hide real complexity from that module. They should not be exported from a global `platform.ts`. This also matches the migration principle that ports belong beside their consumer and should be introduced with actual behavior, not as empty generic packages. 

## There is no separate “Revision system”

A revision is simply a successful session turn that points to a `CodeVersionId`.

```ts
type Turn = {
  id: TurnId;
  author: UserId;
  status: "running" | "succeeded" | "failed";
  version?: CodeVersionId;
};
```

That removes an entire service from the mental model.

```text
CodeVersion = immutable files
Turn        = one user request
Revision    = a successful turn
Session     = ordered turns
```

## There is no generic “provider” abstraction

The mapping is straightforward:

```text
GitHub       → internal Code source/publisher adapters
Bitbucket    → internal Code source/publisher adapters
GCS          → internal Code source/publisher adapters
Upload       → internal Code source adapter

Cloudflare   → Sandboxes implementation
Docker       → Sandboxes implementation

Pi           → Agents implementation
Claude Code  → Agents implementation
other agent  → Agents implementation

D1 / R2 / DO → private Code or Sessions storage mechanisms
```

**Git itself is not a platform system or provider.** It is a private transport and publication mechanism used by GitHub or Bitbucket adapters.

## What normal usage looks like

```ts
const base = await platform.code.import(
  alice,
  githubSource,
);

const session = await platform.sessions.create({
  user: alice,
  workspace,
  version: base,
  agent: codingAgent,
});

const turn = await platform.sessions.send({
  user: alice,
  session,
  message: "Add retry handling.",
});

for await (const event of platform.sessions.watch(alice, turn)) {
  console.log(event);
}
```

Another workspace member can continue it:

```ts
await platform.sessions.send({
  user: bob,
  session,
  message: "Now add tests for the retry limit.",
});
```

And Bob can explicitly publish the current code somewhere completely different:

```ts
const current = await platform.sessions.get(bob, session);

await platform.code.publish({
  user: bob,
  version: current.version,
  target: bitbucketTarget,
  message: "Add bounded payment retries",
});
```

The session does not know GitHub or Bitbucket. The agent does not know either provider. The sandbox does not know which source produced the files. Publication is not part of running the agent.

That is the whole platform.

I rewrote the contract as this four-system model:

[Download `platform-simple.ts`](sandbox:/mnt/data/platform-simple.ts)
