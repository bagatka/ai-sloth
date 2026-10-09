# Patterns

One problem, one solution. This file lists the established way to solve each recurring problem.

- **Follow an entry.** Before solving a problem listed here, follow its entry and the shape of
  its example.
- **Add a missing entry.** If a recurring problem isn't listed, choose a solution deliberately,
  implement it once, and add an entry in the same change.
- **Change an entry, don't fork it.** If an entry is wrong for a case, propose changing it, with
  a migration path for existing code. Never add a second way "just here."
- **Real code beats snippets.** Where an entry names a canonical example, that file is the
  specification. Elsewhere the snippet is, until the first real implementation exists; then add
  its path here.

Examples use `Users` and `Workspaces` modules. Snippets omit `using` lines and put several types
together for brevity; real files list their usings (implicit usings are off) and hold one
top-level type each (entry 27).

---

## 1. Explicit over implicit

Code says what it does where it does it. A contributor who doesn't know .NET's conventions
should be able to read any file and see every conversion, registration, and default.

- **No implicit conversions.**
  - Results and other union values are created with `new`: `new Result<UserProfile>(profile)`,
    `new Result(error)`. Unions allow an implicit conversion from each case; BAG0006 refuses it.
  - Typed IDs are created with `New()` and `From(...)`.
  - No type declares an `implicit operator`.
- **No assembly scanning.** Every DI registration, EF configuration, type conversion, reaction,
  and endpoint group is a visible line in the code.
- **Types are written out.**
  - No `var`.
  - Object creation names its type: `new RenameUser(...)`, not `new(...)` (BAG0005).
- **Accessibility is always written,** including `public` on interface members.
- **Implicit usings are off.** Each file lists its own `using` directives, and unused ones fail
  the build.
- **Endpoint parameters state their source:** `[FromRoute]`, `[FromQuery]`, `[FromBody]`,
  `[FromServices]`. Only `ClaimsPrincipal` and `CancellationToken` are bound without an
  attribute.
- **Never rely on `ToString()` of a complex type.** Convert with a named method or property
  (`actor.ToLogValue()`, `id.Value`).
- **Never rely on defaults that differ between contexts.**
  - JSON always uses `FoundationJson.Options`.
  - Culture and time zone are always explicit (entries 2 and 19).
- **No hidden runtime behavior.** No `dynamic`, and no reflection-driven behavior in production
  code beyond what the framework itself requires.
- **One step per statement.** A statement awaits, calls, saves, or decides; not several at once.
  Canonical example: `src/ControlPlane/Modules/Users/Bagatka.AiSloth.Users/Features/UsersApi.SignIn.cs`.
  - An `await` is its statement's whole expression (BAG0001): `await X;`, `T x = await X;`,
    `x = await X;`, `return await X;`, or `=> await X`. Never inside a condition, an argument, a
    `foreach` header, a switch arm, or a larger expression: await into a local, then use it.
  - Conditions only read values computed before them: no save or other side effect inside an
    `if`, `switch`, or ternary condition.
  - Name conditions that don't read as a sentence on their own:
    `bool otherSignInWon = saved.Error == ModuleDbContextExtensions.AlreadyExists;`. Conditions
    like `user is null` and `result.Failed` stay inline.
  - Check `result.Failed` itself in the `if`, not a bool computed from it: nullable analysis
    doesn't see through a local bool, so `Output` and `Error` would no longer compile unchecked
    (entry 11).
  - A `while` condition may read the next item: `while (await stream.MoveNextAsync())`,
    `while (reader.TryRead(out T item))`. Prefer `await foreach` where the source offers it
    (`ReadAllAsync` on channels and gRPC streams).
- **No `out` in our own APIs.** A method returns a nullable when absence is the only other
  outcome (`MachineProvider.ParseLocation` returns `MachineId?`), a `Result` when it can fail for
  reasons, and a bool only when the outcome is just whether it happened (`OutputJournal.TryAppend`).
  - Our methods declare no `out` parameters (BAG0003), except where an override, an interface,
    or `Deconstruct` dictates the signature.
  - A platform `Try…(out …)` call is its own statement (BAG0002):
    `bool parsed = Guid.TryParse(text, CultureInfo.InvariantCulture, out Guid id);`. When several
    places need it, a helper returns a nullable instead (`Json.Property` in `Bagatka.Harnesses`).
  - Dictionary lookups use `GetValueOrDefault`, and membership uses `ContainsKey` (BAG0007).
- **No local functions** (BAG0004), except in top-level programs (`Program.cs`, `AppHost.cs`). A
  helper is a private method, even when only one method calls it.
- **One deliberate exception: global query filters** for tenant isolation and soft delete,
  because forgetting them is a security bug. Opting out with `IgnoreQueryFilters()` requires a
  comment explaining why.

## 2. Same behavior on every machine

Developers work under Polish, German, British, American, Russian, and other settings, and
servers run under whatever their image defines. The code must build, test, and run identically
everywhere.

### Culture

- **Machine-readable text uses the invariant culture, explicitly.** This covers JSON, logs,
  storage, cache keys, URLs, file names, configuration, and anything code parses back.
  String comparisons use `StringComparison.Ordinal` or `OrdinalIgnoreCase`.
- **Human-readable text uses the recipient's culture, passed explicitly.** This covers emails,
  documents, and exports. The culture is a `CultureInfo` built from the recipient's settings.
  Never the current culture, and no request-localization middleware choosing a culture
  implicitly.
- **Never hardcode separators or formats.**
  - No number literals like `"1,5"` parsed at runtime.
  - No `Replace(",", ".")`, and no `Split('.')` on numbers.
  - Numbers cross the API as JSON numbers, and dates as ISO 8601.
- **Interpolation, concatenation, and `ToString()` format with the current culture.** For machine
  text, use `string.Create(CultureInfo.InvariantCulture, $"...")` instead.
- **Culture-sensitive string methods always get an explicit argument.** `StartsWith`,
  `EndsWith`, `IndexOf`, `Compare`, `ToUpper`, and `ToLower` take an explicit comparison or
  culture, or use the `Invariant` variants.

```csharp
decimal amount = decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);
string cacheKey = string.Create(CultureInfo.InvariantCulture, $"users:{userId.Value}:page:{pageNumber}");
string code = input.ToUpperInvariant();
bool isSame = string.Equals(left, right, StringComparison.Ordinal);
string forRecipient = invoice.Total.ToString("N2", recipientCulture);
```

- **The host pins its culture to invariant,** so anything that slips through behaves the same on
  every server. Canonical example: `src/ControlPlane/Bagatka.AiSloth.WebApi/Program.cs`.
- **`InvariantGlobalization` stays off.** Human-facing output needs real cultures, passed
  explicitly.
- **Enforcement.**
  - CA1304, CA1305, CA1307, CA1309, CA1310, CA1311, MA0011, and MA0074 make culture and
    comparison arguments mandatory; MA0075 and MA0076 do the same for concatenation and
    interpolation. All are build errors.
  - Reading `CultureInfo.CurrentCulture`, `CurrentUICulture`, or `Thread.CurrentCulture` is banned.
  - Tests run under `tr-TR`, which has comma decimals and the dotless `ı` (entry 25). They are the
    backstop for culture-dependent behavior inside libraries the analyzers can't see.

### Files and environment

- **Text files.** UTF-8 without a byte order mark. `.gitattributes` normalizes line endings to LF.
- **Paths.** Build them with `Path.Combine`. Never hardcode `\` or `/` as separators.
- **Casing.** Reference files and folders with their exact casing. Linux is case-sensitive,
  while Windows and macOS usually aren't.
- **Time zones.** See entry 19.
- **Toolchain.** `./dev` (or the `.agents/setup` hook) provides the pinned SDK and sets
  `DOTNET_ROOT`, so a host .NET install never leaks in.

## 3. Interfaces and base classes

An interface earns its place when at least one of these holds:

- **It is a module contract** (`I<Module>Api`). That's a boundary, even with one implementation
  today.
- **Several real implementations exist or are committed to.** For example, `ISandboxProvider` has
  one implementation per compute backend, all passing the same conformance suite.
- **A generic mechanism needs a compile-time contract instead of reflection.** Examples are
  `ITypedId<T>` and `IReaction<TEvent>`.

Never as a marker, never "for future swapping," and never one interface per class.

Base classes are used only where the framework requires them: `DbContext`, `BackgroundService`,
`ValueConverter`, `JsonConverter<T>`. Share behavior through composition or static helpers, not
inheritance. There is no `Entity`, `BaseService`, `BaseEndpoint`, or `BaseTest`, and an
architecture test fails if a class derives from another class in this solution.

## 4. Closed sets: unions and enums

- **A union when the cases carry different data.** `Result<T>` is a value or an `Error`; `Actor`
  is a user, a system process, or anonymous.
- **An enum when the cases are labels.** `ErrorKind`, or the status of a sandbox.
- **Switch exhaustively, without a discard arm.** A new case or member then fails the build
  wherever it is ignored (CS8509). For enums, naming every member is enough: CS8524, which would
  demand a discard for undefined values, is off.
- **Enums start at 1,** so `default` is never a valid member. They are stored and serialized as
  strings.
- **Unions are structs,** so `default(Result<T>)` exists. Reading one throws, because creating it
  was a bug.
- **Create union values with `new`** (entry 1).

Canonical examples: `src/Foundation/Bagatka.Foundation/Actor.cs` and `ErrorKind.cs`.

## 5. Module contract

```csharp
namespace Bagatka.AiSloth.Users.Contracts;

public interface IUsersApi
{
    public Task<UserSummary?> FindAsync(Actor actor, UserId id, CancellationToken ct);
    public Task<IReadOnlyList<UserSummary>> GetManyAsync(Actor actor, IReadOnlyCollection<UserId> ids, CancellationToken ct);
    public Task<Result<UserProfile>> GetProfileAsync(Actor actor, UserId id, CancellationToken ct);
    public Task<Result> RenameAsync(Actor actor, RenameUser command, CancellationToken ct);
}

public sealed record RenameUser(UserId UserId, string DisplayName);
public sealed record UserSummary(UserId Id, string DisplayName);
public sealed record UserProfile(UserId Id, string DisplayName, DateTimeOffset CreatedAt);
```

- **One interface per module.** `I<Module>Api` lists every feature, and every caller uses it.
- **Signature shape.** `Actor` first, `CancellationToken ct` last, always async.
- **Inputs.**
  - State-changing features take one input record.
  - A command, a request the module may refuse, is named verb plus object, after the feature:
    `RenameUser`, `StartChat`. A fact the caller already established is named for what it is:
    `SignInAsync` takes a `VerifiedIdentity`, which a token proved. Never a bare verb such as
    `SignIn`, which doesn't say whether it is an action, an input, or an outcome.
  - Inputs carry raw values. Parsing into value types happens inside the module.
- **Outputs.**
  - Anything that can fail in an expected way returns `Result` or `Result<T>`.
  - Anything shown in lists gets a batch read or a `Page<T>`.
  - A stream returns `Task<Result<IAsyncEnumerable<T>>>`: the feature authorizes and validates
    first, then streams. Cancelling the token ends the stream.
- **Contracts are plain data.** They hold interfaces, records, unions, enums, typed IDs, events,
  and errors. No `IQueryable`, no entities, no EF or ASP.NET types, no logic.
- **References.** A Contracts project references `Bagatka.Foundation`, plus the Contracts of
  modules its own module asks, for their typed IDs. Nothing else, with one known exception
  (`ARCHITECTURE.md`, "Dependency rules").
- **Contracts are documented.** Every public member has an XML doc comment; the build fails
  without one.
- **Size is a signal.** If the interface no longer fits on one screen, either the module is too
  big or the contract should split into a few cohesive interfaces implemented by the same class,
  for example by caller: `INooksApi` for people and agents, `INookDaemonsApi` for the daemon
  endpoint only.

## 6. Module registration

```csharp
public static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services, UsersSettings settings)
    {
        services.AddSingleton(settings);
        services.AddModuleDbContext<UsersDbContext>(UsersDbContext.Schema);
        services.AddSingleton<IUsersApi, UsersApi>();
        services.AddReaction<WorkspaceDeleted, OnWorkspaceDeleted>();
        return services;
    }
}
```

Canonical example: `src/ControlPlane/Modules/Workspaces/Bagatka.AiSloth.Workspaces/WorkspacesModule.cs`.

- **One public type.** `<Module>Module` is the only public type in a module project; its settings
  record lives next to it and is public too, because the host constructs it (entry 20).
- **The table of contents.** Every registration is a visible line here, so this file shows
  everything the module wires up.
- **Callable from anywhere.** `I<Module>Api` is registered once, as a singleton, so a request, a
  job, a reaction, or another module's background work injects it alike, as it would a client of a
  module in another process. Nothing in a module asks the container for a service
  (`GetRequiredService`, scopes): its dependencies are its constructor's.

## 7. Features

One feature = one contract method = one file in `Features/`. Each file contributes one method
to the module's partial `<Module>Api` class.

```csharp
// UsersApi.cs: the front door. Dependencies only; no logic, no state.
internal sealed partial class UsersApi(IDbContextFactory<UsersDbContext> databases, TimeProvider time, ILogger<UsersApi> logger) : IUsersApi
{
}
```

```csharp
// Features/UsersApi.RenameUser.cs
internal sealed partial class UsersApi
{
    public async Task<Result> RenameAsync(Actor actor, RenameUser command, CancellationToken ct)
    {
        // 1. Authorize
        if (!actor.Is(command.UserId))
        {
            return new Result(Error.Forbidden);
        }

        // 2. Parse input: after the Failed check the compiler knows Output is set
        Result<DisplayName> name = DisplayName.Parse(command.DisplayName);
        if (name.Failed)
        {
            return new Result(name.Error);
        }

        // 3. Load, in a context of this call's own
        await using UsersDbContext db = await databases.CreateDbContextAsync(ct);
        User? user = await db.Users.SingleOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null)
        {
            return new Result(UsersErrors.NotFound);
        }

        // 4. Decide: the entity owns the rule and adds its event to the outbox
        Result renamed = user.Rename(name.Output, db.Outbox);
        if (renamed.Failed)
        {
            return renamed;
        }

        // 5. Commit once: the change and its events are saved in one transaction
        return await db.SaveAsync(ct);
    }
}
```

Canonical examples: `Features/WorkspacesApi.CreateWorkspace.cs` and the query helper shared through
`WorkspacesApi.cs`, in `src/ControlPlane/Modules/Workspaces/Bagatka.AiSloth.Workspaces/`.

- **Shape.** State-changing features follow five steps: authorize, parse, load, decide, commit.
  Reads authorize, then project (entry 13).
- **Naming.** The file is named `<Module>Api.<Feature>.cs`: `Features/UsersApi.RenameUser.cs`
  implements `RenameAsync`. The type name comes first because each file holds part of that type.
- **No rules in features.** A feature that needs an `if` about business meaning is holding a
  rule that belongs in an entity or value type.
- **Helpers.**
  - A helper used by one feature is a private method in that feature's file.
  - A helper shared by several features is a private method in `<Module>Api.cs`.
- **No state.** `<Module>Api` holds nothing beyond its constructor dependencies. A growing
  constructor means the module is doing too much.
- **No HTTP.** Features never touch `HttpContext`, `IResult`, or status codes. Modules don't
  reference ASP.NET Core, so the compiler enforces this.

## 8. Typed IDs

Canonical example: `src/Foundation/Bagatka.Foundation/UserId.cs`.

```csharp
[JsonConverter(typeof(TypedIdJsonConverter<OrderId>))]
public readonly record struct OrderId : ITypedId<OrderId>
{
    private OrderId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static OrderId New() => new OrderId(Guid.CreateVersion7());

    public static OrderId From(Guid value) => new OrderId(value);
}
```

- **Where they live.** Every entity has a typed ID declared in its module's Contracts. `UserId`
  is the exception: it lives in `Bagatka.Foundation`, because `Actor` needs it.
- **Why the interface exists.** `ITypedId<T>` declares `static abstract T From(Guid)`, so one
  generic EF converter (`TypedIdConverter<T>`, in `Bagatka.Foundation.Modules`) and one generic JSON converter
  (`TypedIdJsonConverter<T>`) work for every ID type, checked by the compiler rather than
  discovered by reflection.
- **Registration is explicit and local.**
  - JSON: the attribute on the type.
  - EF: one line per ID type in the DbContext (entry 13).
- **Creating IDs.** New IDs come only from `New()`, which produces UUIDv7: time-ordered and
  index-friendly. The constructor is private, and `Guid.NewGuid()` is banned.
- **Rebuilding IDs.** `From(Guid)` is used only at the edges: route values and storage.
- **Raw values.** Use `.Value` wherever a raw value is needed. Never rely on `ToString()`.
- **Cross-module references.** Other modules store foreign IDs as typed values, never as
  foreign keys.

## 9. Value types and validation

Parse, don't validate. Raw input becomes a value type once, at the start of a feature. After
that, the type guarantees validity.

```csharp
internal sealed record DisplayName
{
    public const int MaxLength = 100;

    private DisplayName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<DisplayName> Parse(string? input)
    {
        string trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > MaxLength)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxLength} characters.");
            return new Result<DisplayName>(Error.Validation("displayName", message));
        }

        return new Result<DisplayName>(new DisplayName(trimmed));
    }
}
```

```csharp
// Data/DisplayNameConverter.cs: stored values go back through Parse, so invalid data fails loudly
internal sealed class DisplayNameConverter() : ValueConverter<DisplayName, string>(
    displayName => displayName.Value,
    value => FromStored(value))
{
    private static DisplayName FromStored(string value)
    {
        Result<DisplayName> parsed = DisplayName.Parse(value);
        if (parsed.Failed)
        {
            throw new InvalidOperationException("A stored display name is invalid: " + parsed.Error.Message);
        }

        return parsed.Output;
    }
}
```

Canonical examples: `Model/WorkspaceName.cs` and `Data/WorkspaceNameConverter.cs` in the Workspaces
module.

- **When to create one.** A value gets a type when it has a rule: format, length, range, or
  normalization. Values without rules stay primitives.
- **Limits are written once.** The type's constants are reused by the EF registration. Never
  repeat a limit as a literal.
- **Several inputs.** Parse all of them, then combine them into one validation error with every
  field. `Result.Combine` isn't implemented yet; add it to Foundation with the first feature
  that parses several inputs, in this shape:
  ```csharp
  Result<(DisplayName Name, EmailAddress Email)> inputs =
      Result.Combine(DisplayName.Parse(command.DisplayName), EmailAddress.Parse(command.Email));
  if (inputs.Failed)
  {
      return new Result(inputs.Error);
  }
  ```
- **Never silently truncate or "fix" input.** Reject it with a clear message. If shortening is a
  product rule, make it a named operation on its own type.
- **Field names.** Validation errors use the camelCase name of the input field.
- **Visibility.** Value types are `internal` unless another module must construct them; then
  they move to Contracts.

## 10. Entities

```csharp
internal sealed class User
{
    // Used by Register and by EF: parameter names match property names.
    private User(UserId id, DisplayName displayName, DateTimeOffset createdAt)
    {
        Id = id;
        DisplayName = displayName;
        CreatedAt = createdAt;
    }

    public UserId Id { get; private set; }
    public DisplayName DisplayName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public static User Register(DisplayName displayName, TimeProvider time, IOutbox outbox)
    {
        User user = new User(UserId.New(), displayName, time.GetUtcNow());
        outbox.Add(new UserRegistered(user.Id, user.CreatedAt));
        return user;
    }

    public Result Rename(DisplayName displayName, IOutbox outbox)
    {
        if (DeactivatedAt is not null)
        {
            return new Result(UsersErrors.Deactivated);
        }

        DisplayName = displayName;
        outbox.Add(new UserRenamed(Id, displayName.Value));
        return new Result(new Success());
    }
}
```

Canonical examples: `Model/Workspace.cs` in the Workspaces module, and `Model/Nook.cs` in the Nooks
module for an entity with a lifecycle.

- **Plain classes.** No base class.
- **State is protected.**
  - All setters are private, and state changes only through entity methods.
  - Creation goes through a static factory.
  - EF uses the private constructor whose parameter names match the properties.
- **Inputs and outputs.** Methods take value types, not raw input. They return `Result` when a
  rule can reject the change.
- **Events are explicit.**
  - A method that emits events takes `IOutbox` as a parameter and adds them itself.
  - The signature shows that events are emitted, and no caller can skip them.
- **Collaborators.** What an entity needs for a decision or effect (`TimeProvider`, `IOutbox`)
  is passed as a method argument. Entities never resolve services, never store them, and never
  call other modules.
- **Methods must earn their place.** They exist for real rules. Data without rules doesn't get
  invented methods.
- **References.** Entities in other modules are referenced by typed ID only. Children always
  changed with their parent are loaded with it.

## 11. Errors and results

Canonical examples: `src/Foundation/Bagatka.Foundation/Result{T}.cs`, `Result.cs`, `Error.cs`.

```csharp
public static class UsersErrors
{
    public static readonly Error NotFound = Error.NotFound("users.not_found", "User not found.");
    public static readonly Error Deactivated = Error.Conflict("users.deactivated", "User is deactivated.");
}
```

```csharp
Result done = new Result(new Success());
Result failed = new Result(UsersErrors.NotFound);
Result<UserProfile> found = new Result<UserProfile>(profile);
Result<UserProfile> missing = new Result<UserProfile>(UsersErrors.NotFound);

// Check Failed, then read Error inside the branch and Output after it.
if (found.Failed)
{
    return new Result(found.Error);
}

UserProfile profile = found.Output;
```

- **Created with `new`.** `new Result(new Success())`, `new Result(error)`,
  `new Result<T>(value)`. No factories, no implicit conversions.
- **Read with `Failed`, everywhere.** Modules and edges alike check `result.Failed` directly in
  an `if`, then read `Error` in the branch and `Output` after it.
  - The compiler tracks the check: reading `Output` or `Error` without it is a nullable warning,
    so the build fails. It doesn't track a check stored in another bool, so test `Failed` itself.
  - `Output` throws on a failed or default result. For a value-type `T` the compiler can't warn,
    so that throw is the only guard.
- **Values vs exceptions.** Expected outcomes (invalid input, not found, conflict, forbidden)
  are `Error` values. Exceptions are for bugs and infrastructure failures.
- **What an `Error` carries.** A `Kind`, a stable `Code`, a developer-facing English `Message`,
  and, for validation only, field errors. Clients localize by `Code`, never by `Message`.
- **Creating errors.** Only through `Error`'s factories: `Error.Validation(field, message)`,
  `Error.NotFound(code, message)`, `Error.Conflict(code, message)`, `Error.Forbidden`, and
  `Error.Unauthorized`.
- **Where errors are declared.**
  - Errors callers may branch on are declared once, in Contracts, as `<Module>Errors`.
  - Generic errors come from Foundation.
- **HTTP mapping.** The WebApi maps kinds to HTTP in one place (`Bagatka.Foundation.Web`), as an
  exhaustive switch over `ErrorKind`:

  | Kind | HTTP |
  |---|---|
  | Validation | 400, with field errors |
  | Unauthorized | 401 |
  | Forbidden | 403 |
  | NotFound | 404 |
  | Conflict | 409 |
  | unhandled exception | 500, no internal details, trace ID included |

- **Don't reveal existence.** When the actor shouldn't learn whether something exists, return
  `NotFound`, not `Forbidden`.
- **Logging.** Expected errors aren't logged as errors. Unhandled exceptions are logged once,
  by the global handler.
- **Catching.** Catch exceptions to turn them into errors only at known boundaries:
  concurrency conflicts in `SaveAsync`, and Sdk clients.
- **In `sloth`, a command ends at its first failure** with a sentence on standard error and exit
  code 1 (2 when called wrong). A host that refuses or can't be reached throws
  `HttpRequestException` carrying the host's own words, which `Sloth.RunAsync` prints; a failure
  sloth decides itself is written with `Terminal.FailAsync` where it is decided. Canonical example:
  `src/Cli/Bagatka.AiSloth.Cli/HostApi.cs`.
- **Known gaps.** Every case the code doesn't handle, too rare to earn handling or not built yet,
  is marked once, where it would be handled: `// Not handled: <case>; <what happens today or what
  handling it would take>.` Nothing else lists them, so the change that handles one removes its
  comment. A rare case (AGENTS.md, "Proportional handling") throws `InvalidOperationException` with
  a message saying what is off, and reaches the general handler like any bug. Example:
  `InspectRequiredAsync` in `src/Sandboxing/Bagatka.Sandboxing.Docker/DockerSandboxProvider.cs`.

## 12. Actor and authorization

Canonical example: `src/Foundation/Bagatka.Foundation/Actor.cs`.

- **Every contract method takes an `Actor`.** It is a union of `UserActor`, `SystemActor`, and
  `AnonymousActor`, created with `Actor.ForUser(userId)`, `Actor.ForSystem("<module>.<process>")`,
  or `Actor.Anonymous`.
- **The WebApi authenticates; modules authorize.**
  - Every call carries a session token the host issued (`Authorization: Bearer`). The WebApi asks
    Users whose it is (`IUsersApi.AuthenticateAsync`), creates the actor (`principal.ToActor()`),
    and requires authentication by default.
  - Sessions start only at the WebApi's sign-in endpoints: with a code, or through the host's
    optional OpenID Connect provider, whose ID token the WebApi validates as its client. Users maps
    the provider's issuer and subject to a `UserId` on first sign-in; no code depends on which
    provider it is.
  - End-to-end tests run a small fake provider at the HTTP boundary; the sign-in and its validation
    are the real ones.
  - Canonical examples: `src/ControlPlane/Bagatka.AiSloth.WebApi/SessionAuthentication.cs`,
    `src/ControlPlane/Bagatka.AiSloth.WebApi/Endpoints/SignInEndpoints.cs`, and
    `src/Foundation/Bagatka.Foundation.Web/ActorPrincipals.cs`.
  - It decides no other permissions.
- **The owner of the data owns the permission rule.** That module checks the rule first. Other
  modules ask it.
- **Access levels come from Workspaces.** A feature asks
  `workspaces.GetAccessAsync(actor, Resource.Nook(id.Value))` (or `Resource.Workspace(id)`) and
  compares the answer with the level it needs: null is the module's not-found error, so nobody
  learns that something exists, and a lower level is `Error.Forbidden`. Read sees, Write works,
  Manage decides who else has access. A module that lets the control plane's own processes in
  handles `SystemActor` itself, because access is given to people. Canonical example:
  `FindNookAsync` in `src/ControlPlane/Modules/Nooks/Bagatka.AiSloth.Nooks/NooksApi.cs`.
- **Rules switch over the actor's cases exhaustively.** A new kind of actor then fails the build
  in every rule that doesn't handle it.
- **Identity, not permissions.** The actor carries who is calling, never what they may do.
- **Agents.** Inside its nook, a chat's agent is a process Chats runs as `system:chats.harness`.
  An `AgentActor` comes with MCP, when agents reach the public API: rules decide what agents may do
  like any other case, and sensitive operations an agent attempts (inviting members, deleting,
  billing, permissions) need a human's confirmation. Whose rights it carries is decided then: with
  several people steering one turn, "whoever sent the current message" is not one person.
- **Passing actors along.**
  - Calls between modules on behalf of a user pass that user's actor along.
  - Reactions and jobs use a named system actor. Grep `ForSystem` to find every one of them.
- **Logging an actor.** `actor.ToLogValue()` returns `user:<id>`, `system:<name>`, or
  `anonymous`. It is the only way an actor appears in logs.

## 13. Persistence

```csharp
internal sealed class UsersDbContext(DbContextOptions<UsersDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public IOutbox Outbox => new DbContextOutbox(Set<OutboxMessage>());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("users");
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<UserId>().HaveConversion<TypedIdConverter<UserId>>();
        configurationBuilder.Properties<DisplayName>()
            .HaveConversion<DisplayNameConverter>()
            .HaveMaxLength(DisplayName.MaxLength);
    }
}
```

Canonical examples: `Data/WorkspacesDbContext.cs` in the Workspaces module, and
`Data/MachinesDbContext.cs` in the Machines module for one with an outbox.

- **One DbContext per module.** Each module has one `internal` `<Module>DbContext`, with its own
  schema and its migrations history table in that schema, on the one database the host registers
  (`AddModuleDatabase`). `AddModuleDbContext` registers a pooled `IDbContextFactory<T>`.
- **Names.** Tables and columns are snake_case (EFCore.NamingConventions, applied by
  `UseModuleDatabase`), so hand-written SQL needs no quotes.
- **One operation, one context.** Every operation creates its context from the factory and
  disposes it: a feature, a job's work on one item, a reaction, a stream's every read. A helper
  that needs it takes it as a parameter.
- **Everything is listed.** Every entity configuration and every type conversion is a visible
  line in the DbContext. No scanning.
- **Writes.**
  - Load the entity, call its method, then `await db.SaveAsync(ct)` once per feature.
  - `SaveAsync` (`Bagatka.Foundation.Modules`) returns `Result` and turns concurrency conflicts
    into `Conflict`.
  - Outbox rows live in the same DbContext, so the change and its events commit together.
- **Reads.** Project straight to contract DTOs:
  ```csharp
  public async Task<Result<UserProfile>> GetProfileAsync(Actor actor, UserId id, CancellationToken ct)
  {
      if (!actor.Is(id))
      {
          return new Result<UserProfile>(UsersErrors.NotFound); // don't reveal that other profiles exist
      }

      UserProfile? profile = await db.Users
          .Where(u => u.Id == id)
          .Select(u => new UserProfile(u.Id, u.DisplayName.Value, u.CreatedAt))
          .SingleOrDefaultAsync(ct);

      if (profile is null)
      {
          return new Result<UserProfile>(UsersErrors.NotFound);
      }

      return new Result<UserProfile>(profile);
  }
  ```
- **Loading rules.** When loading an entity has rules, a named method on the DbContext owns
  them: `db.LoadOrderAsync(id, ct)`.
- **Global query filters** are the one sanctioned implicit mechanism (entry 1).
- **Columns.** Every string column has an explicit maximum length. Enums are stored as strings.
- **Lazy loading is off.**
- **Bulk operations.** `ExecuteUpdate` and `ExecuteDelete` bypass entity rules and events. Use
  them only for maintenance on data without rules, with a comment saying why.
- **Raw SQL.** Only through EF's parameterized APIs (`FromSql`, `SqlQuery`), and only for a
  measured need. Never concatenate SQL.
- **Secrets at rest.** A secret a module must keep, such as an agent account's key, is stored
  sealed with AES-256-GCM under the module's own key, derived from the deployment's
  (`EncryptionSettings`, section `Encryption`), bound to its row's ID so it can't be moved to
  another row. It is decrypted only to hand to whoever uses it, in a record whose text
  form leaves it out, and never logged. A secret the module only checks, such as a daemon's or a
  machine's token, is stored as its SHA-256 hash instead. `SecretBox` (`Bagatka.Foundation.Modules`)
  seals and opens; each module registers its own, keyed by its schema, so no module ever opens with
  another's key: `services.AddKeyedSingleton(SecretsDbContext.Schema, new SecretBox(encryption, SecretsDbContext.Schema))`
  and `[FromKeyedServices(SecretsDbContext.Schema)] SecretBox box`. Canonical example: the Secrets
  module (`src/ControlPlane/Modules/Secrets`).
  Codes people pass on once, such as a machine's registration code or an invite, come from
  `OneTimeCode` (Foundation) and are kept only as its hash.
- **Concurrency.** Entities that can be edited concurrently get a concurrency token in their
  configuration.
- **Files too big for a row** live in object storage (`IObjectStorage`, `src/Storage`), under a
  prefix that names their owner, such as `nooks/<nook ID>/…` or `people/<user ID>/…`; a module's row
  refers to them. The object is written before the row that refers to it, and the row deleted before
  the object, so an object may be left over but a row never points at nothing. Objects aren't
  encrypted here: the storage is protected like the database. Canonical example: checkpoints
  (`src/ControlPlane/Modules/Nooks/Bagatka.AiSloth.Nooks/Checkpoints.cs`).

## 14. Migrations

- **One migration set per module.** Each module keeps its migrations in `Data/Migrations`,
  created against its own DbContext.
- **Applied migrations are history.** Never edit a migration that has been applied anywhere
  shared.
- **Snapshot conflicts.** Never hand-edit the model snapshot. After a merge conflict in it,
  remove your migration, rebase, and regenerate.
- **Applying them.**
  - `migrate`: the WebApi run with that single argument applies every module's migrations, the
    instance lease's in `Bagatka.Foundation.Modules` included (`ModuleDatabases.MigrateAsync`), and
    exits. Deployed, it runs as the container app's init
    container before each replica starts; run locally, the AppHost runs it as the `migrations`
    resource. The WebApi itself never migrates on startup.
- **Generating them.** Each module has a design-time factory (`Data/<Module>DbContextFactory.cs`),
  so `dotnet ef` needs no configuration. Migrations are generated code: `.editorconfig` marks them
  so, and analyzers skip them.
- **The version before keeps working.** A deploy runs the previous version against the new schema
  for a moment, so a migration never breaks it, such as with a new required column it doesn't set.
- **Destructive changes use expand–contract across releases.** Add the new shape, migrate the
  data, switch the code, and remove the old shape in a later release.

## 15. Integration events and reactions

```csharp
// Users.Contracts: a fact, named in the past tense
public sealed record UserRegistered(UserId UserId, DateTimeOffset OccurredAt);
```

```csharp
// Billing module: Reactions/OnUserRegistered.cs
internal sealed class OnUserRegistered(BillingDbContext db) : IReaction<UserRegistered>
{
    public async Task<Result> HandleAsync(UserRegistered integrationEvent, CancellationToken ct)
    {
        bool alreadyHandled = await db.Customers.AnyAsync(existing => existing.UserId == integrationEvent.UserId, ct);
        if (alreadyHandled)
        {
            return new Result(new Success()); // delivery is at-least-once
        }

        Customer customer = Customer.CreateFor(integrationEvent.UserId);
        db.Customers.Add(customer);
        return await db.SaveAsync(ct);
    }
}
```

Canonical example: `MachineRemoved` in `Bagatka.AiSloth.Machines.Contracts`, added by `Machine.Remove`,
and `Reactions/OnMachineRemoved.cs` in the Nooks module. Delivery is in
`src/Foundation/Bagatka.Foundation.Modules/Events/`.

- **Registration.** The publisher calls `services.AddOutbox<<Module>DbContext>()`, and its DbContext
  maps `OutboxMessage` with `OutboxMessageConfiguration`. A reacting module calls
  `services.AddReaction<TEvent, TReaction>()`.
- **Event shape.**
  - Events are immutable records in the publisher's Contracts, named `<Noun><PastTenseVerb>`.
  - They carry IDs plus the facts consumers commonly need.
- **Who emits.** Only entities add events, through the `IOutbox` they receive as a parameter.
  The rows commit with the change.
- **Delivery and idempotency.** After commit, Foundation's dispatcher, on the active instance,
  delivers events at least once: it looks every second, oldest first, and runs each reaction in turn.
  Every reaction is idempotent, preferably check-then-act on a key protected by a
  unique index.
- **Failures.** A reaction that fails, by its result or an exception, has the event delivered again
  with growing waits, its other reactions too, up to ten minutes apart. After ten failures the event
  is parked in its outbox and logged for an operator. Events are stored under their type's full
  name, so renaming one loses those published but not yet delivered.
- **Reaction shape.**
  - One reaction per file in `Reactions/`, named `On<Event>`, registered explicitly in
    `<Module>Module`. Its parameter is named `integrationEvent`.
  - A reaction commits only its own module's data.
- **Changing events.** Changes are additive. A breaking change is a new event type, and the old
  one keeps being published until no reaction uses it.
- **Events are announcements, not requests.** Use them for "something happened." To get an
  answer, call a contract.

## 16. Calling other modules

```csharp
// Workspaces module: Features/WorkspacesApi.AddMember.cs
internal sealed partial class WorkspacesApi
{
    public async Task<Result> AddMemberAsync(Actor actor, AddMember command, CancellationToken ct)
    {
        // Ask: awaited, read-only
        UserSummary? user = await users.FindAsync(actor, command.UserId, ct);
        if (user is null)
        {
            return new Result(WorkspacesErrors.UnknownUser);
        }

        Workspace? workspace = await db.LoadWorkspaceAsync(command.WorkspaceId, ct);
        if (workspace is null)
        {
            return new Result(WorkspacesErrors.NotFound);
        }

        // Decide on our own data only; the entity checks the actor's role
        Result added = workspace.AddMember(user.Id, actor, db.Outbox);
        if (added.Failed)
        {
            return added;
        }

        // Commit once; other modules react to MemberAdded
        return await db.SaveAsync(ct);
    }
}
```

- **Inject only the contract.** Inject `I<Other>Api`, and never anything else from another
  module.
- **Order of operations.** Ask first, then change only your own data, then let events carry the
  consequences.
- **Calling another module's state-changing method** is allowed only when its effect stands on
  its own even if your commit then fails. Otherwise, commit yours and let the other module react.
- **Atomic invariants mean one module.** If an invariant must hold atomically across two
  modules, they are one module.
- **Lists use batch methods.** Never call another module once per item in a loop.
- **One hop deep.** A method other modules call should not itself call further modules to answer.
- **No cycles.** The asks graph is acyclic; the reverse direction is an event.

## 17. WebApi endpoints

```csharp
// Bagatka.AiSloth.WebApi/Endpoints/UsersEndpoints.cs
internal static class UsersEndpoints
{
    public sealed record RenameRequest(string DisplayName);

    public static RouteGroupBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder users = app.MapGroup("/users").WithTags("Users");
        users.MapGet("/{id:guid}", GetProfile);
        users.MapPut("/{id:guid}/display-name", Rename);
        return users;
    }

    private static async Task<Results<Ok<UserProfile>, ProblemHttpResult>> GetProfile(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] IUsersApi api,
        CancellationToken ct)
    {
        Result<UserProfile> result = await api.GetProfileAsync(principal.ToActor(), UserId.From(id), ct);
        return result.ToOk();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Rename(
        [FromRoute] Guid id,
        [FromBody] RenameRequest request,
        ClaimsPrincipal principal,
        [FromServices] IUsersApi api,
        CancellationToken ct)
    {
        RenameUser command = new RenameUser(UserId.From(id), request.DisplayName);
        Result result = await api.RenameAsync(principal.ToActor(), command, ct);
        return result.ToNoContent();
    }
}
```

Canonical example: `src/ControlPlane/Bagatka.AiSloth.WebApi/Endpoints/WorkspacesEndpoints.cs`.

- **One file per module.** Each module gets `Endpoints/<Module>Endpoints.cs`, with named handler
  methods, explicit binding attributes, and written return types.
- **Endpoints translate only.** Build the actor, map the request to a command, call the
  contract, and map the `Result` to HTTP. No logic, no DbContext, no direct Sdk calls.
- **Rate limits.** An endpoint that does expensive work for anyone, or for one person many times,
  names one of the policies in `RateLimits.cs`: `.RequireRateLimiting(RateLimits.Starts)` and
  `.ProducesProblem(StatusCodes.Status429TooManyRequests)`. Limits are host settings with defaults.
- **Request records** live in the endpoints file and use the same field names as the command
  they map to.
- **Responses.** Return contract DTOs directly. Add a WebApi response record only when the
  client needs a different shape.
- **Curate.** Not every contract method gets a route.
- **Every public route is a tool.** Agents and MCP clients use exactly the public API, so its
  documentation, which flows from the XML docs into OpenAPI and MCP, is written for both people and
  models: what the operation does, when to use it, and what its errors mean. Prefer batch
  operations and filterable queries to one-at-a-time calls.
- **Public surface.** Today every route is public. Once modules run as separate services, routes
  for other services stay on the private network, and only the Gateway exposes public ones
  (`ARCHITECTURE.md`).
- **Routes.** Use plural kebab-case resources with IDs as segments. State changes that aren't
  plain updates become sub-resources (`POST /orders/{id}/cancel`).
- **Authentication is required by default** through the fallback policy. Anonymous endpoints
  say `AllowAnonymous()` explicitly.
- **JSON.** The WebApi configures JSON with `FoundationJson.Configure`, and clients use
  `FoundationJson.Options`: camelCase, enums as strings, numbers only as numbers, and required
  constructor arguments and non-nullable values enforced.
- **Streams.** A contract stream becomes server-sent events (`TypedResults.ServerSentEvents`), one
  event type per union case, such as `output` and `exit` when watching a process. The stream
  flushes the response headers before its first item, so a client watching something quiet knows
  it is connected. Canonical example: `Watch` in `src/ControlPlane/Bagatka.AiSloth.WebApi/Endpoints/ChatsEndpoints.cs`.
- **Screens.** A response that combines several modules lives in `Composition/`. Inbound
  webhooks verify their signature with the Sdk client, then call the owning module with a
  system actor.
- **OpenAPI.** The document is generated by ASP.NET Core's built-in OpenAPI support from the
  typed results, and committed as `src/ControlPlane/Bagatka.AiSloth.WebApi/openapi.json`;
  `HostJourney` fails when the host's differs, so every change to the API shows in review.

## 18. Pagination

```csharp
public sealed record PageRequest(string? Cursor, int Limit);
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);
```

Canonical example: `Keyset.TakePage` and `Keyset.ToPage` in `Bagatka.Foundation.Modules`, used by
`Features/WorkspacesApi.ListMyWorkspaces.cs`.

- **Keyset pagination with an opaque cursor.** Order by a unique, stable key; UUIDv7 IDs already
  order by creation.
- **Bounded size.** Foundation clamps `Limit` to a maximum of 200. The WebApi supplies the
  default page size explicitly.
- **No offset pagination,** and no total counts unless the product needs them.

## 19. Time

- **One clock.** Inject `TimeProvider`. Every API that reads the machine clock or the machine
  time zone is banned: `DateTime.Now`, `UtcNow`, and `Today`; `DateTimeOffset.Now` and `UtcNow`;
  `TimeZoneInfo.Local`; `ToLocalTime()`.
- **Storage types.** Instants are `DateTimeOffset` in UTC, calendar dates are `DateOnly`, and
  durations are `TimeSpan`.
- **A user's time zone is explicit data.** It is an IANA ID (`Europe/Warsaw`) stored in their
  settings, converted with `TimeZoneInfo.FindSystemTimeZoneById`.
- **Formatting.**
  - For machines: ISO 8601 (`"O"` with `CultureInfo.InvariantCulture`).
  - For humans: an explicit culture and an explicit time zone.
- **Tests** use `FakeTimeProvider`.

## 20. Configuration

Owners declare what they need; hosts decide where it comes from.

```csharp
// The owner, for example a sandbox provider: an immutable record that validates itself.
public sealed record DockerSandboxSettings
{
    public DockerSandboxSettings(Uri endpoint, string scope)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        Endpoint = endpoint;
        Scope = scope;
    }

    public Uri Endpoint { get; }
    public string Scope { get; }
}
```

```csharp
// The host's Program.cs: the only code that reads configuration, one visible line per owner.
DockerSandboxSettings docker = builder.Configuration.GetRequired<DockerSandboxSettings>("Sandboxing:Docker");
NooksSettings nooks = builder.Configuration.GetRequired<NooksSettings>("Modules:Nooks");

builder.Services
    .AddDockerSandboxProvider(docker)
    .AddNooksModule(nooks);
```

- **Hosts read configuration; owners receive values.** Sdk clients, sandbox providers, and
  modules never see `IConfiguration`, section names, or environment variables. Each declares one
  settings record and takes it as a registration argument, so it can move to another product or
  to NuGet unchanged, and tests simply construct it.
- **Settings validate themselves.** The record's constructor rejects missing and invalid values.
  Configuration binding passes `null` for missing values instead of failing, so the constructor is
  what protects you. An invalid setting is a deployment bug, so it throws at startup.
- **`GetRequired<T>(section)`** (`src/Aspire/Bagatka.ServiceDefaults/ConfigurationExtensions.cs`)
  binds a section through the record's constructor and fails startup, naming the section, when the
  section is missing, a value is missing or invalid, or a key is unknown, which catches typos. Every
  constructor parameter needs a property of the same name, which is how the binder matches keys
  (`DockerSandboxSettings.Endpoint`).
- **Values that change while running are rare, and only in product code.** The owner declares them
  as a separate options class and reads `IOptionsMonitor<T>.CurrentValue` at each decision,
  without caching it. Its registration takes `Action<OptionsBuilder<T>>`, so the host must choose
  the source, and can bind a reloading provider such as Azure App Configuration or AWS AppConfig.
  The owner registers the validation. Anything wired at startup (connection pools, HTTP base
  addresses) is never live; changing it takes a restart.
- **Credentials that rotate are objects, not strings.** Pass a credential that refreshes itself,
  such as Azure's `TokenCredential`. A vendor with only static API keys rotates them by restarting
  the service, until a real need for live rotation appears.
- **Sections.** Hosts use `Modules:<Module>`, `Sandboxing:<Backend>`, and `Sdk:<Vendor>`, so a
  reader can find any owner's settings.
- **Number format.** Configuration binds with the invariant culture, so decimal values in
  configuration always use `.` as the separator.
- **Secrets never go in committed files.** Use user-secrets locally, and environment variables
  or a secret store when deployed.
- **Prefer a sensible default to a setting.** Add a setting only for values that genuinely
  differ between environments.

## 21. Logging and telemetry

```csharp
// Log.cs in each module
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} deactivated by {Actor}")]
    public static partial void UserDeactivated(ILogger logger, Guid userId, string actor);
}

// At the call site, every value is converted explicitly:
Log.UserDeactivated(logger, user.Id.Value, actor.ToLogValue());
```

```text
# LoggerParameterTypes.txt
Actor;System.String
UserId;System.Guid
```

- **One catalog per module.** Each module declares its log messages in `Log.cs` as
  source-generated `[LoggerMessage]` methods.
- **One vocabulary of log fields.** Every placeholder name is registered once in
  `LoggerParameterTypes.txt` with the types it may carry; an unregistered name or a wrong type
  fails the build (MA0124, MA0135). `UserId` is the same field, with the same type, in every
  module.
- **Primitive types only.** Register `string`, `bool`, integer types, `decimal`, `Guid`,
  `DateTimeOffset`, or `TimeSpan`. Complex types and enums are converted at the call site with a
  call you can see.
- **What never goes in logs.** Log IDs only: never personal data, secrets, or tokens.
- **What to log.** Log what operators need. Expected errors aren't logged as errors, and
  unhandled exceptions are logged once.
- **Levels.** Information records routine events, such as a nook falling asleep, for local runs
  and tests. Deployed hosts keep Warning and above (`appsettings.json`), so every line there is
  worth an operator's look; to investigate, raise a category with `Logging__LogLevel__<category>`.
- **Where it goes.** A host with a PostHog project (`PostHog:Host`, `PostHog:ProjectToken`) sends
  its logs, traces, and metrics there over OTLP, and every exception it logs at Error or worse
  becomes an error tracking issue (`src/ControlPlane/Bagatka.AiSloth.WebApi/ExceptionsToPostHog.cs`,
  through `Bagatka.PostHog`), so an Error log is a failure someone sees. Without a project, a host
  sends nothing anywhere.
- **Product events.** What people do and what it led to, for product analytics: a module that
  sees it happen captures a `ProductEvent` through `IProductEvents` (`Bagatka.Foundation`), named
  in the past tense and snake_case, with the person, the workspace, and plain facts: kinds,
  counts, durations in seconds, outcomes, yes or no. Never what people wrote, names, repositories,
  or secrets. Canonical example: `CaptureTurnEnded` in
  `src/ControlPlane/Modules/Chats/Bagatka.AiSloth.Chats/Harness/ChatRunner.cs`. The events so far:
  - people: `signed_up` (method), `invite_accepted` (access, resource), `github_connected`,
    `agent_account_added` (kind, workspace_account), `repository_added`, `machine_added`;
  - chats: `chat_started` (harness, provider, account_kind, repositories, copy, from_checkpoint),
    `message_sent` (harness, first, proposal), `turn_ended` (harness, outcome, failure: start,
    agent_error, or agent_exited, seconds, first_action_seconds, model, account_kind,
    checkpoint_saved), `setup_ended` (succeeded, seconds, scripts, from_ready_copy);
  - nooks: `nook_started` (provider, how: new, ready_copy, woken, restored, replaced, recovered, or
    resumed, seconds, image), `nook_failed` (provider, how);
  - files: `changes_pushed` (pull_request, repositories, failed), `files_downloaded`
    (from_checkpoint, one_source);
  - `sloth` sends `command_ran` (command, exit_code, seconds, version, platform) and its crashes
    itself (`src/Cli/Bagatka.AiSloth.Cli/Sloth.Usage.cs`), and `slothd` its crashes, as `slothd`
    with the nook's ID (`src/Daemon/Bagatka.AiSloth.Daemon/Program.cs`).

  A new event is added here in the same change.
- **Traces and metrics.**
  - OpenTelemetry is configured by `Bagatka.ServiceDefaults` in every host.
  - A module adds an `ActivitySource` or `Meter` named `Bagatka.AiSloth.<Module>` only when it
    has something specific to measure; the WebApi collects every `Bagatka.AiSloth.*` meter.
    Canonical example: `src/ControlPlane/Modules/Chats/Bagatka.AiSloth.Chats/Harness/ChatsMeter.cs`.

## 22. External APIs

- **Official first.** Prefer the vendor's official .NET SDK. Otherwise, write a client in
  `src/Sdk` following `src/Sdk/README.md`.
- **Vendors stay inside modules.** A vendor is used from a module's internals.
- **Widely used vendors get one owner.** Prefer a module that reacts to existing events over one
  that everyone has to call.

## 23. Background work

- **Ownership.** Background work is owned by a module, lives in `Jobs/`, and is registered
  explicitly in `<Module>Module`.
- **Shape.**
  - Implement it as a `BackgroundService` driven by a `PeriodicTimer`. A job that features need
    to run sooner exposes `Wake()` and waits for that or its interval instead
    (`Jobs/NookLifecycle.cs` in the Nooks module).
  - Create one context per item, and pass `stoppingToken` everywhere.
- **One failure never stops a job.** Catch per item and per pass, log, and let the next pass retry.
- **Bounded batches.** Each iteration processes a limited batch.
- **The active instance runs it.** A job's `ExecuteAsync` returns
  `active.RunAsync(WorkAsync, stoppingToken)` (`ActiveInstance`, `Bagatka.Foundation.Modules`): its
  work starts when the instance takes the lease and stops when it hands over, so no two instances
  run it at once. Work is still idempotent, since an instance may stop at any point and the next one
  starts from what was saved. Canonical example: `Jobs/NookLifecycle.cs` in the Nooks module.
- **Long-lived connections move with the work.** A stream that lasts, such as a watch or a daemon's
  or machine's connection, ends on `ActiveInstance.Leaving`, and its other end resumes on the active
  instance from where it was: after the last event, from the next offset, or by dialing again.
- **Actor.** Jobs use a named system actor when calling contracts.
- **Schedulers.** If you need cron schedules or durable job queues, choose one library, record
  it here, and use it everywhere.

## 24. Caching

- **No cache without a measured need.**
- **When there is one,** use `HybridCache`, only inside the module that owns the data. That
  module invalidates the cache whenever it writes.
- **Keys** follow `<module>:<entity>:<id>[:<variant>]`, built with
  `string.Create(CultureInfo.InvariantCulture, ...)`.
- **Permission-dependent results** are never cached under a key that doesn't include the actor.

## 25. Testing

Test what people and agents rely on, through the surface they use: the public API.

- **Journeys** (`tests/Bagatka.AiSloth.EndToEndTests/Journeys`). Aspire's test builder starts the
  real app: PostgreSQL, the WebApi, and the Docker sandbox provider creating real nooks that run a
  real daemon. A journey is one test: a real flow of someone using AiSloth through the public API and
  `sloth`, as named steps that check what matters along the way, so what is tested is what people
  and agents can do. Parts that wait on clocks run at the same time. A fake OpenID Connect provider
  in the test process (`FakeIssuer`) signs people in through the host's real sign-in, the host's
  first person takes its setup code, and the AppHost's `sandbox-scope` parameter keeps each run's
  nooks apart. `sloth` runs in the test process the same way, against the same host (`SlothCli`).
  Canonical example: `ChatJourney`.
- **When journeys run.** On main, which deploys only once they pass. Day to day, build and run the
  journey closest to the change:
  `dotnet test --project tests/Bagatka.AiSloth.EndToEndTests --filter-class "*.ChatJourney"`.
- **Waiting has a deadline.** A journey that waits for background work polls with a bounded
  patience, so a broken one fails instead of hanging.
- **Every feature** extends the journey it belongs to, or starts one for a new flow: its normal
  path, its consequential failure, and who may use it. Rules of single inputs, page cursors, and
  ordering details don't get steps; the code keeps them.
- **Isolation without resets.** Each journey's people work in workspaces of their own, so journeys
  run in parallel against one running app, with no database cleanup.
- **Fakes only for paid or external services,** at the HTTP boundary: the test host gives the
  client settings that point at a local fake. Everything we run ourselves, such as PostgreSQL, the
  daemon, and Docker, is real.
- **Smoke tests.** The same journeys run against a deployed environment or real cloud providers,
  after every deploy and on demand before a release.
- **Lower-level tests only where they pay:**
  - the conformance suite every sandbox provider passes (`Bagatka.Sandboxing.ConformanceTests`),
    run with a change to a provider;
  - the architecture tests (`tests/Bagatka.AiSloth.ArchitectureTests`), which enforce
    `ARCHITECTURE.md`;
  - dense logic with many edge cases, such as git push policy or protocol parsing.

  Don't unit-test what journeys already cover.
- **Time.** Time-dependent behavior, such as idle suspension and timeouts, takes its durations from
  settings, which tests make short. Lower-level tests use `FakeTimeProvider`.
- **One framework.** xUnit v3 on Microsoft Testing Platform, with xUnit's own assertions. Run
  everything with `dotnet test --solution AiSloth.slnx`.
- **Test project shape.** `tests/Directory.Build.props` makes every test project an xUnit
  executable running under `tr-TR`; a test project's `.csproj` only references what it tests.
- **Culture.** `tests/xunit.runner.json` sets `"culture": "tr-TR"` for every test process, and a
  canary test (`TestCultureTests`) fails if that setting stops applying.
- **Never mock** the DbContext or our own modules.
- **Test names state behavior,** for example `Rename_fails_for_deactivated_user`. Underscores are
  allowed in test projects only.

## 26. Build, analyzers, banned APIs

The build files are the specification; this entry says what each one owns.

| File | Owns |
|---|---|
| `Directory.Build.props` | Target framework, nullable, warnings as errors (compiler, MSBuild, NuGet), `AnalysisMode` `All`, documentation file, lock files |
| `Directory.Packages.props` | The single version of every package, and the analyzers every project gets |
| `.editorconfig` | Code style, and every analyzer rule that is turned off, each with its reason |
| `BannedSymbols.txt` | APIs nobody may call, each with what to use instead |
| `LoggerParameterTypes.txt` | Log placeholder names and their types (entry 21) |
| `global.json` | SDK version and test runner |
| `dotnet-tools.json` | The pinned Aspire CLI (`dotnet aspire update` upgrades it with the AppHost) and `dotnet-ef` |
| `NuGet.Config` | The only package source, with every package mapped to it |
| `tests/Directory.Build.props` | The shape of every test project |
| `analyzers/Bagatka.Analyzers` | Our own rules, `BAG0001`–`BAG0007`: the code shape of entry 1 that no off-the-shelf analyzer checks. Every project gets it through `Directory.Build.props`; each rule has a test in `tests/Bagatka.Analyzers.Tests` |

- **Rules are turned off only in `.editorconfig`,** with a one-line reason, scoped to a path when
  only some files need it. Never `#pragma` or `[SuppressMessage]`.
- **New packages need clear net value.** Call them out in the change summary.
- **Repeated mistakes become bans.** When a mistake repeats, ban the API, raise an analyzer rule,
  or add a `BAG` rule with its test, rather than adding another paragraph here.

## 27. Files and naming

- **One top-level type per file,** and the file is named after it: `UserId.cs`. Generic types use
  braces: `Result{T}.cs`. Partial types add a suffix: `UsersApi.RenameUser.cs`. MA0048 enforces
  this.
- **Generated protocol types go through an alias in the control plane:**
  `using Wire = Bagatka.AiSloth.DaemonProtocol.V1;`, then `Wire.Hello`. Generated messages share
  names with our own types (`Error`, `Hello`, `ProcessExited`), and the alias keeps both readable. A
  second protocol in the same file gets an alias named for its messages (`Calls` in
  `MachineEndpoint`). The daemon and the CLI speak their protocols natively and import them directly.
- **Nested types stay nested** only when they belong to their parent alone, such as an endpoint's
  request record.

## 28. Recipes

### Add a feature

1. Add the method, with its command and DTO records, to `I<Module>Api`.
2. Implement it in `Features/<Module>Api.<Feature>.cs`, following the five-step shape.
3. Put any new rule in a value type or entity. Methods that emit events take `IOutbox`. Add
   errors callers may branch on to `<Module>Errors`.
4. If clients need it, add a named handler in `Endpoints/<Module>Endpoints.cs`.
5. Test it end to end through the public API: normal path, consequential failure, authorization.
6. Update the module README if the contract's purpose, events, or dependencies changed.

### Add a module

0. **Check first.** Should an existing module own this capability?
1. **Create the two projects** under `src/ControlPlane/Modules/<Module>/`.
   - `Bagatka.AiSloth.<Module>.Contracts` references `Bagatka.Foundation`.
   - `Bagatka.AiSloth.<Module>` references its Contracts and `Bagatka.Foundation.Modules`, and
     declares `InternalsVisibleTo` for its test project.
2. **Add the standard files:**
   - `<Module>Module`, and `<Module>Settings` if the module needs settings.
   - `<Module>Api` (the partial root).
   - `<Module>DbContext`, with its schema, its outbox, and every configuration and conversion
     listed.
   - `Log.cs`.
3. **Write its map.** Copy `docs/templates/module-readme.md` into the module folder and fill it in.
4. **Wire it up.** Add both projects to `AiSloth.slnx` and reference them from
   `tests/Bagatka.AiSloth.ArchitectureTests` (a test fails until you do). Register the module in
   `Program.cs`, and add an endpoints file if clients need one.
5. **Update the shared docs.** Add the module to the Module map in `ARCHITECTURE.md`, and its
   terms to `GLOSSARY.md`.
6. **Create the initial migration.**

### Add an external API client

Follow `src/Sdk/README.md`.

## 29. Addresses people supply

The control plane calls URLs people choose: an agent account's endpoint today, git remotes and
webhooks later. Unguarded, anyone could make it reach this host, its private network, or a cloud's
metadata service, and read the answer.

- **The shape is checked where the data is owned.** The owner parses the URL into a value type
  (`ApiEndpoint` in AgentAccounts): absolute, http or https, no credentials, query, or fragment.
- **The network is checked where the call is made.** The HTTP client's handler connects through
  `PublicNetworks.ConnectAsync` (`Bagatka.Foundation.Web`), which resolves the name and connects only
  to a public address, for every connection. The caller requires https and doesn't follow redirects.
- **Private networks are a deployment's choice:** a setting of the caller that turns the guard off,
  for a company's own endpoint inside its network, a model on the same machine, or tests.
- **Canonical example:** the model gateway's client in
  `src/ControlPlane/Bagatka.AiSloth.WebApi/Program.cs`, with `ModelGatewaySettings` and
  `Endpoints/ModelGatewayEndpoints.cs`.
