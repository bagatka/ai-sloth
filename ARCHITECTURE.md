# Architecture

The one-page map of the backend: what the parts are, what each owns, and how they connect.
How to solve recurring problems lives in `PATTERNS.md`, vocabulary in `GLOSSARY.md`, and each
module's details in its own `README.md`.

> Template: replace `Company` and `Product`, then fill in the Module map at the end.
> Examples use `Users` and `Organizations` only because nearly every product has them.

## Shape

A modular monolith: one deployable host composed of modules. Each module owns one capability,
its data, and one public contract. Everything that calls a module (the HTTP gateway, another
module, a background job, a test) calls that same contract. Nothing depends on a module's
internals, so any module can later run as its own service without its callers changing.

```
HTTP ──▶ Gateway ──▶ I<Module>Api ──▶ Module
                                        ├── asks ─────▶ I<Other>Api      (awaited reads)
                                        ├── reacts ◀── <Other> events    (after commit, via outbox)
                                        ├── owns ─────▶ its schema       (own DbContext)
                                        └── uses ─────▶ Platform, Sdk clients
```

## Solution layout

```
Company.Product.sln
Directory.Build.props              shared build settings for every project
Directory.Packages.props           the single version of every package
BannedSymbols.txt                  APIs nobody may call
docs/templates/                    templates (module README)
src/
  Company.Product.WebApi/          gateway + composition root
    Program.cs
    Endpoints/<Module>Endpoints.cs
    Composition/                   responses assembled from several modules
  Modules/
    <Module>/
      README.md                    the module's map
      Company.Product.<Module>.Contracts/
      Company.Product.<Module>/
        <Module>Module.cs          the only public type: registration
        <Module>Api.cs             partial root of the contract implementation
        Features/                  one file per contract method
        Model/                     entities and value types
        Data/                      DbContext, mappings, migrations
        Reactions/                 handlers for other modules' events
        Jobs/                      background work, if any
        Log.cs                     the module's log messages
  Platform/
    Company.Platform/              primitives usable everywhere, including Contracts
    Company.Platform.Modules/      plumbing for module projects
    Company.Platform.Web/          plumbing for the gateway
  Sdk/
    README.md
    Company.Sdk.<Vendor>/          general-purpose third-party API clients
tests/
  Company.Product.<Module>.Tests/
  Company.Product.WebApi.Tests/
  Company.Product.ArchitectureTests/
```

## Naming rule

`Company.Product.*` belongs to this product. `Company.*` without a product segment is
general-purpose: it never references product code and could be used by another product
unchanged. The name tells you which rules apply before you open the project.

## Parts

### Gateway: `Company.Product.WebApi`

The HTTP edge and the composition root. It has four jobs:

- **Authenticate:** turn a request into an `Actor`.
- **Translate:** map HTTP to contract calls, and `Result`s back to HTTP.
- **Curate:** decide which contract methods are public.
- **Compose:** assemble responses that need several modules.

It owns no business rules and touches no database. It references module projects only to call
their registration in `Program.cs`. Everything else in a module is `internal` and unreachable.

```csharp
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddPlatformWeb(builder.Configuration)
    .AddUsersModule(builder.Configuration)
    .AddOrganizationsModule(builder.Configuration);

WebApplication app = builder.Build();

app.UsePlatformWeb();
app.MapUsersEndpoints();
app.MapOrganizationsEndpoints();

app.Run();
```

### Module: `Company.Product.<Module>` + `.Contracts`

A capability with one contract, `I<Module>Api`.

- **Contracts project:** holds the interface, its command and DTO records, typed IDs,
  integration events, and errors. It contains plain data only and depends on nothing but
  `Company.Platform`, so it can later become a client package.
- **Module project:** holds everything else, all `internal`, except one public registration
  class, `<Module>Module`. It does not reference ASP.NET Core, so HTTP concerns cannot leak
  into business logic.

### Platform: three small projects, split by who may use them

| Project | Used by | Holds |
|---|---|---|
| `Company.Platform` | everyone, including Contracts | `Result`, `Error`, `Actor`, `UserId`, `ITypedId<T>`, `TypedIdJsonConverter<T>`, `Page<T>`, `PageRequest`, `PlatformJson`, `Money` |
| `Company.Platform.Modules` | module projects | `IOutbox` and its database implementation, `TypedIdConverter<T>`, `SaveAsync`, outbox dispatcher, `IReaction<T>`, module registration helpers |
| `Company.Platform.Web` | gateway only | `Result` → HTTP mapping, `ClaimsPrincipal` → `Actor`, exception handler |

Platform is plumbing only. A business concept never goes into Platform. If two modules need
one, one module owns it and exposes it through its contract.

### Sdk: `Company.Sdk.<Vendor>`

General-purpose clients for third-party APIs without a usable official .NET SDK. They are
vendor-shaped, product-agnostic, and used from module internals. Rules are in `src/Sdk/README.md`.

## Dependency rules

| Project | May reference | Must not reference |
|---|---|---|
| Gateway | Contracts, module projects (registration only), `Platform`, `Platform.Web` | module internals (enforced by `internal`) |
| Module | its Contracts, other modules' Contracts, `Platform`, `Platform.Modules`, Sdk clients | other module projects, ASP.NET Core, `Platform.Web` |
| Contracts | `Platform` | everything else |
| `Platform.Modules`, `Platform.Web` | `Platform`, .NET, approved packages | `Company.Product.*` |
| `Platform` | .NET only | everything else |
| Sdk client | `Platform`, .NET, approved packages | `Company.Product.*`, `Platform.Modules`, `Platform.Web` |

The "asks" graph is acyclic: if module A calls `I<B>Api`, B never calls `I<A>Api`. When B
needs to respond to something in A, it reacts to A's events. Reacting to events is the
sanctioned reverse direction.

## Data

- Each module owns a schema in the shared database, named after the module.
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

1. The gateway authenticates the request, builds the `Actor`, and calls the contract
   (`IUsersApi.RenameAsync`).
2. The feature authorizes, parses input into value types, loads the entity, calls its method,
   and commits with `SaveAsync`.
3. Events the entity recorded are written to the module's outbox in the same transaction.
4. The gateway maps the `Result` to an HTTP response.
5. After commit, the dispatcher delivers the events to reactions in other modules. Each
   reaction runs in its own transaction.

## Moving a module into its own service

Do this only for an operational reason: scaling, deployment cadence, isolation, or ownership by
another team. Callers know only the contract, so no caller changes:

1. Host the module in its own process, exposing `I<Module>Api` over an internal transport.
2. The feature authorizes, parses input into value types, loads the entity, and calls its
   method. The method applies the rule and adds its events to the module's outbox.
3. `SaveAsync` commits the change and the outbox rows in one transaction.
4. Move delivery of its events to a message broker.
5. Run the module's contract test suite against the remote implementation.

## Enforcement

The compiler does most of the work:

- `internal` hides module internals.
- Project references define the allowed dependencies.
- Nullable reference types and warnings-as-errors are on everywhere.
- Banned APIs are listed in `BannedSymbols.txt`.

Architecture tests in `tests/Company.Product.ArchitectureTests` find projects by naming
convention, so new modules are covered automatically. They verify that:

- the dependency rules table holds;
- each module project exposes exactly one public type, `<Module>Module`;
- module and Contracts projects do not reference ASP.NET Core;
- Contracts contain only interfaces, records, enums, typed IDs, and error definitions;
- the asks graph (`I<Module>Api` usage between modules) is acyclic;
- `<Module>Api` declares no instance state of its own.
- no type in a `Company.*` assembly declares an implicit conversion operator;
- `[LoggerMessage]` methods take only primitive parameters;
- no `Company.*` type derives from another `Company.*` class.

## Deliberate decisions

These are choices, not omissions. Change them only through `PATTERNS.md`, with a migration path.

- **Modular monolith, not microservices.**
  - Contracts and the compiler already enforce the boundaries.
  - Distribution adds latency, partial failure, and operations cost. Pay that only for a real
    operational reason.
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
- **One contract per module, for every caller.** HTTP lives in the gateway, outside modules.
- **Explicit over implicit.**
  - No implicit conversions, no assembly scanning, no base classes, no `var`.
  - No reliance on the machine's culture or time zone.
  - The single exception is global query filters for tenant isolation and soft delete.

## Module map

Keep this current. Any change that adds, removes, splits, merges, or re-wires a module updates
this table in the same change.

| Module | Owns | Asks | Reacts to | Schema |
|---|---|---|---|---|
| _Users (example)_ | _Accounts and profiles_ | — | — | `users` |
