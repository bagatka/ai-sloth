# Architecture

The one-page map of AiSloth: what the parts are, what each owns, and how they connect. How to
solve recurring problems lives in `PATTERNS.md`, vocabulary in `GLOSSARY.md`, and each module's
details in its own `README.md`.

## Product

AiSloth runs coding agents in on-demand nooks, on the cloud the customer chooses, for people and
agents working together. It is sold as a hosted service first; the same code should later be
deployable by customers on their own cloud.

```
web, mobile, sloth CLI, MCP clients ──▶ control plane ──lifecycle──▶ sandbox provider ──▶ nook
                                              ▲                                           │
                                              └────────── gRPC, dialed out by slothd ◀────┘
```

- **Nook and chats:** the basic unit. A nook is where agents work: an isolated machine with its
  files and processes, owned by a workspace. Its chats are conversations between people and a
  coding agent in it. Underneath, a nook is a provider's sandbox; "sandbox" is a technical term that
  never reaches the product.
- **Sources:** where a nook's files come from: repositories (any git remote) and folders AiSloth
  keeps. A nook mounts any number of them at `/work/<name>`.
- **Projects:** optional groups of nooks, chats, and sources for a team working toward one goal,
  with shared context and a project chat. Nothing depends on them.
- **Control plane:** owns every piece of state and every decision. Today it is one deployable:
  the WebApi host and its modules.
- **Sandbox provider:** creates, suspends, resumes, snapshots, and deletes sandboxes on one compute
  backend. Lifecycle only.
- **Daemon (`slothd`):** runs in every nook, dials out to the control plane, and runs processes for
  it. Everything the control plane does inside a nook goes through the daemon, so a feature is
  built once rather than once per provider, and no provider needs inbound networking.

### Product principles

- **Agents can do whatever their user can.** The public HTTP API is the one curated surface: the
  web app, the CLI, MCP clients, and agents inside nooks all use it, with the same authorization.
  Agents compose its operations into use cases, so most use cases never become code. Our job is
  good primitives: batch operations, filterable queries, clear errors, and documentation good
  enough to serve as a tool description. An agent acts for the user who sent the current message,
  and sensitive operations it attempts need a human's confirmation.
- **Multiplayer by default.** Several people and agents work in the same workspace, nook, and chat
  at once. Humans and agents are both actors, every change records its actor, and anything shown to
  several people updates live.
- **Nooks never run directly on anyone's operating system.** In AiSloth's cloud and on people's own
  machines alike, a nook is a container or a virtual machine, so an agent can't damage the computer
  it runs on.
- **Nooks are untrusted.** The agent, the repository, and its dependencies may be hostile, and they
  can read anything inside a nook, including the daemon's token. Credentials that matter, such as
  git write access and cloud keys, stay in the control plane, which acts for a nook under policy:
  pushes, for example, go through a control-plane git proxy that allows only the agent's own
  branches.
- **Running work never stops for us.** Deploys, restarts, and network blips never stop a process in
  a nook; agents may run for days. Suspension is invisible to callers.
- **Nothing delivered is lost.** A nook is disposable, so what people can't afford to lose lives
  outside it: the control plane saves every chat event as it arrives, and every turn ends with a
  checkpoint of the nook's files and the harness's session state in object storage. Losing a nook,
  through a full disk, a crash, or a deletion, costs at most the turn in progress. Risks we can see
  coming, such as a nearly full disk, pause new work until a person confirms, with the risk
  explained.
- **Small units, optional groups.** A nook and its chats work on their own; a project groups them
  without anything else knowing about projects. Every capability is general-purpose: removing a
  grouping leaves the units working.
- **Return on effort decides scope.** A feature that buys a little and slows every later change is
  declined. Prefer the version that removes concepts.

## Parts

| Part | Projects | Purpose | Status |
|---|---|---|---|
| WebApi | `Bagatka.AiSloth.WebApi` | HTTP host and composition root of the control plane | Sign-in, public API for users, workspaces, and nooks, daemon endpoint, `migrate` |
| Modules | `Bagatka.AiSloth.<Module>` + `.Contracts` | Product capabilities, one contract each | Users, Workspaces, and Nooks built |
| Sandboxing | `Bagatka.Sandboxing` + `.<Provider>` | Provider contract, conformance tests, one project per compute backend | Contract and Docker provider |
| Daemon | `Bagatka.AiSloth.DaemonProtocol`, `Bagatka.AiSloth.Daemon` (`slothd`) | The protocol, and the Native AOT process in every nook | Built |
| CLI | `Bagatka.AiSloth.Cli` (`sloth`) | Native AOT command line over the public HTTP API; its machine mode runs nooks on people's own computers | Planned |
| Foundation | `Bagatka.Foundation` (+ `.Modules`, `.Web`) | Plumbing: results, errors, actors, typed IDs | Built |
| Object storage | `Bagatka.ObjectStorage` + `.<Backend>` | Store and read objects by key: folder versions, checkpoints, harness state | Planned |
| Sdk | `Bagatka.Sdk.<Vendor>` | Clients for vendor APIs without an official .NET SDK | Docker Engine |
| Aspire | `Bagatka.AiSloth.AppHost`, `Bagatka.ServiceDefaults` | Local orchestration; defaults every service host shares | Built |

The web and mobile apps are not in this repository. They use the same public HTTP API as the CLI.

## Solution layout

```
AiSloth.slnx
Directory.Build.props              shared build settings for every project
Directory.Packages.props           the single version of every package
BannedSymbols.txt                  APIs nobody may call
LoggerParameterTypes.txt           log placeholder names and their types
docs/templates/                    templates (module README)
src/
  Aspire/
    Bagatka.AiSloth.AppHost/       local orchestration
    Bagatka.ServiceDefaults/       OpenTelemetry, health, service discovery
  ControlPlane/
    Bagatka.AiSloth.WebApi/        HTTP host + composition root
      Program.cs
      Endpoints/<Module>Endpoints.cs
      Composition/                 responses assembled from several modules
    Modules/
      <Module>/
        README.md                  the module's map
        Bagatka.AiSloth.<Module>.Contracts/
        Bagatka.AiSloth.<Module>/
          <Module>Module.cs        the only public type: registration
          <Module>Api.cs           partial root of the contract implementation
          Features/                one file per contract method
          Model/                   entities and value types
          Data/                    DbContext, mappings, migrations
          Reactions/               handlers for other modules' events
          Jobs/                    background work, if any
          Log.cs                   the module's log messages
  Daemon/
    README.md
    Bagatka.AiSloth.DaemonProtocol/  daemon.proto and the code generated from it
    Dockerfile                     the nook image: slothd under tini
    Bagatka.AiSloth.Daemon/        slothd
  Cli/                             sloth (planned)
  Sandboxing/
    README.md
    Bagatka.Sandboxing/            the provider contract
    Bagatka.Sandboxing.<Backend>/  one provider per compute backend: Docker
  Storage/
    Bagatka.ObjectStorage/         general-purpose object storage contract and backends (planned)
  Foundation/
    Bagatka.Foundation/            primitives usable everywhere, including Contracts
    Bagatka.Foundation.Modules/    plumbing for module projects
    Bagatka.Foundation.Web/        plumbing for WebApi hosts
  Sdk/
    README.md
    Bagatka.Sdk.<Vendor>/          general-purpose third-party API clients
tests/
  Bagatka.AiSloth.EndToEndTests/   the main suite: the real app through its public API
  Bagatka.AiSloth.Daemon.Tests/    the real daemon against a fake control plane
  Bagatka.Sandboxing.ConformanceTests/  one suite every sandbox provider passes
  Bagatka.AiSloth.ArchitectureTests/
  Bagatka.Foundation.Tests/
```

## Naming rule

`Bagatka.AiSloth.*` belongs to this product. `Bagatka.*` without the product segment is
general-purpose: it never references product code and could serve another product unchanged.
The name tells you which rules apply before you open the project.

## Parts in detail

### WebApi: `Bagatka.AiSloth.WebApi`

The HTTP host of the control plane and its composition root. It has four jobs:

- **Authenticate:** turn a request into an `Actor`. It accepts OpenID Connect tokens from the
  issuer named in its settings: WorkOS for the hosted service, and any compliant provider (their
  own WorkOS, Entra ID, Cognito, Zitadel, and others) for self-hosting. Only configuration
  differs; no code knows the provider.
- **Translate:** map HTTP to contract calls, and `Result`s back to HTTP.
- **Curate:** decide which contract methods are public.
- **Compose:** assemble responses that need several modules.

It owns no business rules and touches no database. It references module projects only to call
their registration in `Program.cs`. Everything else in a module is `internal` and unreachable.
It also hosts the daemon endpoint, an HTTP/2-only gRPC endpoint every nook's daemon dials, and will
host the MCP endpoint, which exposes the same public operations as HTTP. Run with the single argument
`migrate`, it applies every module's migrations and exits. Canonical example:
`src/ControlPlane/Bagatka.AiSloth.WebApi/Program.cs`.

```csharp
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// The only place that reads configuration (PATTERNS.md, entry 20).
DockerSandboxSettings docker = builder.Configuration.GetRequired<DockerSandboxSettings>("Sandboxing:Docker");
UsersSettings users = builder.Configuration.GetRequired<UsersSettings>("Modules:Users");
WorkspacesSettings workspaces = builder.Configuration.GetRequired<WorkspacesSettings>("Modules:Workspaces");
NooksSettings nooks = builder.Configuration.GetRequired<NooksSettings>("Modules:Nooks");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddGrpc();
builder.Services
    .AddDockerSandboxProvider(docker)
    .AddUsersModule(users)
    .AddWorkspacesModule(workspaces)
    .AddNooksModule(nooks);

await using WebApplication app = builder.Build();
if (args is ["migrate"])
{
    await ModuleDatabases.MigrateAsync(app.Services, CancellationToken.None);
    return;
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapUsersEndpoints();
app.MapWorkspacesEndpoints();
app.MapNooksEndpoints();
app.MapGrpcService<DaemonEndpoint>().AllowAnonymous();
await app.RunAsync();
```

### Module: `Bagatka.AiSloth.<Module>` + `.Contracts`

A capability with one contract, `I<Module>Api`.

- **Contracts project:** holds the interface, its command and DTO records, typed IDs,
  integration events, and errors. It contains plain data only and depends on nothing but
  `Bagatka.Foundation`, so it can later become a client package.
- **Module project:** holds everything else, all `internal`, except the public registration
  class `<Module>Module` and, when the module needs settings, the `<Module>Settings` record the
  host builds. It does not reference ASP.NET Core, so HTTP concerns cannot leak into business
  logic.

### Nooks, providers, and the daemon

Built: the Docker provider, the daemon and its image, and the Nooks module from creating a nook to
deleting it. Suspension, checkpoints, and templates are next. The maps are
`src/ControlPlane/Modules/Nooks/README.md`, `src/Sandboxing/README.md`, and `src/Daemon/README.md`.
These decisions are fixed:

- **Lifecycle.** A nook is Running, Paused (memory and files kept: it resumes in about a second and
  processes continue), or Stopped (files kept: it resumes in seconds and processes start again).
  Files survive until deletion on every provider; memory is kept where the backend can. Any
  operation resumes a suspended nook first, so callers only notice latency.
- **Providers do lifecycle only.** Every operation is safe to repeat, and every provider passes the
  same conformance suite. `Bagatka.Sandboxing.Docker` comes first: it serves local development, CI,
  and single-machine deployments.
- **Record first, then reconcile.** The Nooks module records a nook before asking a provider to
  create it, so every sandbox at a provider has a record. A reconciler retries what a failed call
  left undone and deletes what shouldn't exist.
- **Processes are detached.** The daemon runs them until they exit or are stopped, keeps their
  output so anyone can watch it from any offset, and reports them again after it reconnects.
- **The daemon dials out,** over a protocol defined once in `daemon.proto`: a small control stream,
  plus one stream per bulk transfer so a busy process never delays instructions. Previews of web
  servers running in a nook will use the same connection.
- **One active control-plane instance for now.** A deploy starts the new instance and switches
  traffic; the old instance tells its daemons to reconnect, then exits. Calls in the few seconds
  between wait for the daemon, as they do for a resume. Several active instances would need each
  instruction routed to the instance holding that nook's connection; that is designed when
  capacity requires it.

### Foundation: three small projects, split by who may use them

| Project | Used by | Holds |
|---|---|---|
| `Bagatka.Foundation` | everyone, including Contracts and Sdk clients | `Result`, `Result<T>`, `Success`, `Error`, `ErrorKind`, `Actor`, `UserId`, `ITypedId<T>`, `TypedIdJsonConverter<T>`; later `Page<T>`, `PageRequest`, `FoundationJson`, `Money` |
| `Bagatka.Foundation.Modules` | module projects | `AddModuleDbContext`, `ModuleDatabases.MigrateAsync`, `TypedIdConverter<T>`, `SaveAsync`, keyset pagination; `IOutbox`, the outbox dispatcher, and `IReaction<T>` come with the first integration event |
| `Bagatka.Foundation.Web` | WebApi hosts | `Result` → HTTP mapping as problem details, `ClaimsPrincipal` → `Actor`; unhandled exceptions use ASP.NET Core's built-in problem details |

Foundation is plumbing only. A business concept never goes into Foundation. If two modules need
one, one module owns it and exposes it through its contract.

### Sdk: `Bagatka.Sdk.<Vendor>`

General-purpose clients for third-party APIs without a usable official .NET SDK. They are
vendor-shaped, product-agnostic, and used from module internals. Rules are in `src/Sdk/README.md`.

### Aspire

- **`Bagatka.AiSloth.AppHost`** runs the control plane and its dependencies on a developer
  machine and in end-to-end tests. It is never deployed. The Aspire CLI is pinned in
  `dotnet-tools.json`, and `dotnet aspire update` upgrades both together.
- **`Bagatka.ServiceDefaults`** gives every service host the same OpenTelemetry, health checks,
  and service discovery: the WebApi today, and each service extracted from it later. It is
  general-purpose.

## Dependency rules

| Project | May reference | Must not reference |
|---|---|---|
| WebApi | Contracts, module projects (registration only), `Foundation`, `Foundation.Modules` (`migrate` only), `Foundation.Web`, `ServiceDefaults`, `DaemonProtocol`, sandbox providers (registration only) | module internals (enforced by `internal`) |
| Module | its Contracts, other modules' Contracts, `Foundation`, `Foundation.Modules`, `Sandboxing`, Sdk clients | other module projects, ASP.NET Core, `Foundation.Web` |
| Contracts | `Foundation`, the Contracts of modules its module asks | everything else |
| Daemon | `DaemonProtocol`, `Foundation`, .NET | everything else |
| `Foundation.Modules`, `Foundation.Web`, `ServiceDefaults` | `Foundation`, .NET, approved packages | `Bagatka.AiSloth.*` |
| `Foundation` | .NET only | everything else |
| `Sandboxing`, `ObjectStorage`, Sdk client | `Foundation`, .NET, approved packages | `Bagatka.AiSloth.*`, `Foundation.Modules`, `Foundation.Web` |
| Sandbox provider, object storage backend | its contract, `Foundation`, its vendor's SDK or Sdk client | `Bagatka.AiSloth.*`, `Foundation.Modules`, `Foundation.Web` |

The "asks" graph is acyclic: if module A calls `I<B>Api`, B never calls `I<A>Api`. When B
needs to respond to something in A, it reacts to A's events. Reacting to events is the
sanctioned reverse direction.

Projects is an extension: no module asks it or reacts to its events, and no other contract mentions
a project. Only the WebApi references its contracts.

## Data

- Each module owns a schema in the shared PostgreSQL database, named after the module.
- That schema is accessed only through the module's own `DbContext` and changed only through
  its own migrations.
- No module reads another module's tables.
- There are no cross-module joins, foreign keys, or transactions.
- Modules store other modules' typed IDs as plain values and learn about changes through events.

## Communication

**Ask synchronously, commit once, react asynchronously.**

- **Ask:** a feature may await another module's contract to read what it needs.
- **Commit once:** a feature writes only its own module's data, in a single `SaveAsync`.
- **React:** consequences in other modules happen through integration events, delivered after
  commit through the outbox.

Every call carries an `Actor`. When acting for a user, pass the user's actor along. Reactions
and jobs use an explicit, named system actor.

## A request, end to end

1. The WebApi authenticates the request, builds the `Actor`, and calls the contract
   (`IUsersApi.RenameAsync`).
2. The feature authorizes, parses input into value types, loads the entity, and calls its
   method. The method applies the rule and adds its events to the module's outbox.
3. `SaveAsync` commits the change and the outbox rows in one transaction.
4. The WebApi maps the `Result` to an HTTP response.
5. After commit, the dispatcher delivers the events to reactions in other modules. Each
   reaction runs in its own transaction.

## Moving a module into its own service

Do this only for an operational reason: scaling, deployment cadence, isolation, or ownership by
another team. Callers know only the contract, so no caller changes:

1. Host the module in its own process, with its own WebApi-style host and
   `Bagatka.ServiceDefaults`, exposing `I<Module>Api` over an internal transport.
2. In other hosts, register a remote implementation of `I<Module>Api` instead of
   `Add<Module>Module`.
3. Move its schema to its own database. It is already isolated.
4. Move delivery of its events to a message broker.
5. Run the module's contract test suite against the remote implementation.

Once there is more than one service, a **Gateway** (a reverse proxy such as YARP) becomes the
only public edge. It authenticates, routes public endpoints to the service that owns them, and
never exposes the internal ones services use to call each other. Until then the WebApi is the
edge, and no Gateway exists.

## Enforcement

The compiler does most of the work:

- `internal` hides module internals, and project references define the allowed dependencies.
- Warnings are errors, `AnalysisMode` is `All`, and Meziantou.Analyzer runs in every project.
  Every rule that is turned off is listed in `.editorconfig` with its reason.
- Banned APIs are listed in `BannedSymbols.txt`, and log placeholders in
  `LoggerParameterTypes.txt`.
- Switches over unions and enums are exhaustive (`PATTERNS.md`, entry 4).

Architecture tests in `tests/Bagatka.AiSloth.ArchitectureTests` read project files and types,
and a test fails if a source project isn't checked by them. Today they verify that:

- general-purpose projects never reference product projects;
- `Bagatka.Foundation` depends only on the base class library;
- no type declares an implicit conversion operator;
- no class derives from another class in this solution;
- tests run under `tr-TR`.

With the first module, they will also verify that:

- the dependency rules table holds;
- each module project exposes only `<Module>Module` and `<Module>Settings`;
- module and Contracts projects do not reference ASP.NET Core;
- Contracts contain only interfaces, records, unions, enums, typed IDs, and error definitions,
  and reference only Foundation and the Contracts of modules their module asks;
- the asks graph (`I<Module>Api` usage between modules) is acyclic;
- `<Module>Api` declares no instance state of its own.

## Deliberate decisions

These are choices, not omissions. Change them only through `PATTERNS.md`, with a migration path.

- **Modular monolith, not microservices.**
  - Contracts and the compiler already enforce the boundaries.
  - Distribution adds latency, partial failure, and operations cost. Pay that only for a real
    operational reason.
- **No Gateway until there are several services.** The WebApi is the edge today.
- **Two projects per module, not Domain/Application/Infrastructure layers.**
  - Layer projects spread one feature across several places without making any wrong write
    impossible.
  - Value types, entities, and `internal` hold the rules instead.
- **EF Core used directly. No repositories, no unit-of-work wrappers.**
  - The `DbContext` already is both.
  - Named load methods cover loading rules.
- **No mediator library.** Callers call the contract. A dispatcher between a caller and the one
  method it calls is a forwarding layer.
- **No mapping library.** Projections are explicit `Select`s and constructors, visible and
  compile-checked.
- **Errors as values, exceptions for faults.** Expected outcomes return a `Result`; exceptions
  are reserved for bugs and infrastructure failures.
- **One contract per module, for every caller.** HTTP lives in the WebApi, outside modules.
- **The daemon carries behavior; providers carry lifecycle.** A new provider implements a small
  contract, and a new nook feature is written once.
- **Standard sign-in, no provider in code.** The WebApi validates OpenID Connect tokens: their
  signature against the issuer's published keys, the issuer, and the audience, which WorkOS adds
  through a JWT template. WorkOS hosts sign-in for the hosted service. Provider-specific extras,
  such as directory sync, would be an optional extension.
- **Explicit over implicit.**
  - No implicit conversions, no assembly scanning, no base classes, no `var`.
  - No reliance on the machine's culture or time zone.
  - The single exception is global query filters for tenant isolation and soft delete.
- **Closed sets are unions or enums, matched exhaustively,** so adding a case breaks every
  place that ignores it.
- **Projects are an extension.** Nooks and chats are complete without them, so a team feature never
  makes the single-person path more complex.
- **Git is a file-history format, not a product concept.** Sources may be repositories or folders,
  and changes leave a nook by download, by saving a folder version, or by pushing a branch.

## Module map

Keep this current. Any change that adds, removes, splits, merges, or re-wires a module updates
this table in the same change.

| Module | Owns | Asks | Reacts to | Schema |
|---|---|---|---|---|
| Workspaces (contract only) | Workspaces, their members and roles | — | — | `workspaces` |
| Nooks (contract only) | Nooks, where each runs, their lifecycle, processes, templates, checkpoints, daemon connections | Workspaces, Sources, Machines | — | `nooks` |
| Sources (planned) | Repositories and folders, their recipes, delivery, push policy | Workspaces | — | `sources` |
| Chats (planned) | ACP conversations in nooks, turns, harness profiles and state | Nooks, Workspaces | — | `chats` |
| Machines (planned) | People's own computers registered to run nooks, their credentials and connections | Workspaces | — | `machines` |
| Projects (planned, extension) | Groups of nooks, chats, and sources, shared context, project chat | Nooks, Chats, Sources, Workspaces | Nooks, Chats | `projects` |
