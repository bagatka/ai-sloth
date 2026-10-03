# Patterns

One problem, one solution. This file lists the established way to solve each recurring problem.

- **Follow an entry.** Before solving a problem listed here, follow its entry and the shape of
  its example.
- **Add a missing entry.** If a recurring problem isn't listed, choose a solution deliberately,
  implement it once, and add an entry in the same change.
- **Change an entry, don't fork it.** If an entry is wrong for a case, propose changing it, with
  a migration path for existing code. Never add a second way "just here."
- **Snippets are the spec for now.** Until real code exists, snippets are the specification.
  Then add the path of the first real implementation as the canonical example.

Examples use a `Users` module because nearly every product has one. Snippets omit `using`
lines for brevity; real files list them (implicit usings are off).

---

## 1. Explicit over implicit

Code says what it does where it does it. A contributor who doesn't know .NET's conventions
should be able to read any file and see every conversion, registration, and default.

- **No implicit conversion operators.**
  - Results are created with `Result.Ok(...)` and `Result.Fail(...)`.
  - Typed IDs are created with `New()` and `From(...)`.
  - No type in this solution declares an `implicit operator`.
- **No assembly scanning.** Every DI registration, EF configuration, type conversion, reaction,
  and endpoint group is a visible line in the code.
- **Types are written out.**
  - No `var`.
  - Object creation names its type: `new RenameUser(...)`, not `new(...)`.
- **Implicit usings are off.** Each file lists its own `using` directives.
- **Endpoint parameters state their source:** `[FromRoute]`, `[FromQuery]`, `[FromBody]`,
  `[FromServices]`. Only `ClaimsPrincipal` and `CancellationToken` are bound without an
  attribute.
- **Never rely on `ToString()` of a complex type.** Convert with a named method or property
  (`actor.ToLogValue()`, `id.Value`).
- **Never rely on defaults that differ between contexts.**
  - JSON always uses `PlatformJson.Options`.
  - Culture and time zone are always explicit (entries 2 and 18).
- **No hidden runtime behavior.** No `dynamic`, and no reflection-driven behavior in production
  code beyond what the framework itself requires.
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
- **Interpolation and `ToString()` format with the current culture.** For machine text, use
  `string.Create(CultureInfo.InvariantCulture, $"...")` instead.
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

- **The host pins its culture to invariant.** `Program.cs` does this, so anything that slips
  through behaves the same on every server:
```csharp
  CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
  CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
```
- **`InvariantGlobalization` stays off.** Human-facing output needs real cultures, passed
  explicitly.
- **Enforcement.**
  - Analyzers CA1304, CA1305, CA1307, CA1309, CA1310, and CA1311 are errors.
  - Reading `CultureInfo.CurrentCulture` or `CurrentUICulture` is banned.
  - Test projects run under `tr-TR`, which has comma decimals and the dotless `ı`, so culture
    bugs fail on every machine.
  - Analyzers don't reliably catch string interpolation, so these tests are the backstop.

### Files and environment

- **Text files.** UTF-8 everywhere. `.gitattributes` normalizes line endings to LF.
- **Paths.** Build them with `Path.Combine`. Never hardcode `\` or `/` as separators.
- **Casing.** Reference files and folders with their exact casing. Linux is case-sensitive,
  while Windows and macOS usually aren't.
- **Time zones.** See entry 18.

## 3. Interfaces and base classes

An interface earns its place when at least one of these holds:

- **It is a module contract** (`I<Module>Api`). That's a boundary, even with one implementation
  today.
- **Two real implementations exist now.** For example, `IOutbox` has a database implementation
  and a test recorder.
- **A generic mechanism needs a compile-time contract instead of reflection.** Examples are
  `ITypedId<T>` and `IReaction<TEvent>`.

Never as a marker, never "for future swapping," and never one interface per class.

Base classes are used only where the framework requires them: `DbContext`, `BackgroundService`,
`ValueConverter`. Share behavior through composition or static helpers, not inheritance. There
is no `Entity`, `BaseService`, `BaseEndpoint`, or `BaseTest`.

## 4. Module contract

```csharp
namespace Company.Product.Users.Contracts;

public interface IUsersApi
{
    Task<UserSummary?> FindAsync(Actor actor, UserId id, CancellationToken ct);
    Task<IReadOnlyList<UserSummary>> GetManyAsync(Actor actor, IReadOnlyCollection<UserId> ids, CancellationToken ct);
    Task<Result<UserProfile>> GetProfileAsync(Actor actor, UserId id, CancellationToken ct);
    Task<Result> RenameAsync(Actor actor, RenameUser command, CancellationToken ct);
}

public sealed record RenameUser(UserId UserId, string DisplayName);
public sealed record UserSummary(UserId Id, string DisplayName);
public sealed record UserProfile(UserId Id, string DisplayName, DateTimeOffset CreatedAt);
```

- **One interface per module.** `I<Module>Api` lists every feature, and every caller uses it.
- **Signature shape.** `Actor` first, `CancellationToken ct` last, always async.
- **Inputs.**
  - State-changing features take one command record named after the feature.
  - Commands carry raw input. Parsing into value types happens inside the module.
- **Outputs.**
  - Anything that can fail in an expected way returns `Result` or `Result<T>`.
  - Anything shown in lists gets a batch read.
- **Contracts are plain data.** They hold interfaces, records, enums, typed IDs, events, and
  errors. No `IQueryable`, no entities, no EF or ASP.NET types, no logic.
- **Size is a signal.** If the interface no longer fits on one screen, either the module is too
  big or the contract should split into a few cohesive interfaces implemented by the same class.

## 5. Module registration

```csharp
public static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<UsersDbContext>(configuration, schema: "users");
        services.AddScoped<IUsersApi, UsersApi>();
        services.AddReaction<OrganizationDeleted, OnOrganizationDeleted>();
        services.AddOptions<UsersOptions>()
            .BindConfiguration("Modules:Users")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return services;
    }
}
```

- **One public type.** `<Module>Module` is the only public type in a module project.
- **The table of contents.** Every registration is a visible line here, so this file shows
  everything the module wires up.

## 6. Features

One feature = one contract method = one file in `Features/`. Each file contributes one method
to the module's partial `<Module>Api` class.

```csharp
// UsersApi.cs: the front door. Dependencies only; no logic, no state.
internal sealed partial class UsersApi(UsersDbContext db, TimeProvider time, ILogger<UsersApi> logger) : IUsersApi
{
}
```

```csharp
// Features/RenameUser.cs
internal sealed partial class UsersApi
{
    public async Task<Result> RenameAsync(Actor actor, RenameUser command, CancellationToken ct)
    {
        // 1. Authorize
        if (!actor.Is(command.UserId))
            return Result.Fail(Error.Forbidden);

        // 2. Parse input
        Result<DisplayName> name = DisplayName.Parse(command.DisplayName);
        if (name.IsError)
            return Result.Fail(name.Error);

        // 3. Load
        User? user = await db.Users.SingleOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null)
            return Result.Fail(UsersErrors.NotFound);

        // 4. Decide: the entity owns the rule and adds its event to the outbox
        Result renamed = user.Rename(name.Value, db.Outbox);
        if (renamed.IsError)
            return renamed;

        // 5. Commit once: the change and its events are saved in one transaction
        return await db.SaveAsync(ct);
    }
}
```

- **Shape.** State-changing features follow five steps: authorize, parse, load, decide, commit.
  Reads authorize, then project (entry 12).
- **Naming.** The file is named after the feature: `RenameUser.cs` implements `RenameAsync`.
- **No rules in features.** A feature that needs an `if` about business meaning is holding a
  rule that belongs in an entity or value type.
- **Helpers.**
  - A helper used by one feature is a local function inside that feature's method.
  - A helper shared by several features is a private method in `<Module>Api.cs`.
- **No state.** `<Module>Api` holds nothing beyond its constructor dependencies. A growing
  constructor means the module is doing too much.
- **No HTTP.** Features never touch `HttpContext`, `IResult`, or status codes. Modules don't
  reference ASP.NET Core, so the compiler enforces this.

## 7. Typed IDs

```csharp
[JsonConverter(typeof(TypedIdJsonConverter<OrderId>))]
public readonly record struct OrderId(Guid Value) : ITypedId<OrderId>
{
    public static OrderId New() => new OrderId(Guid.CreateVersion7());
    public static OrderId From(Guid value) => new OrderId(value);
}
```

- **Where they live.** Every entity has a typed ID declared in its module's Contracts. `UserId`
  is the exception: it lives in `Company.Platform`, because `Actor` needs it.
- **Why the interface exists.** `ITypedId<T>` exists so that one generic EF converter
  (`TypedIdConverter<T>`) and one generic JSON converter (`TypedIdJsonConverter<T>`) work for
  every ID type, checked by the compiler rather than discovered by reflection.
- **Registration is explicit and local.**
  - JSON: the attribute on the type.
  - EF: one line per ID type in the DbContext (entry 12).
- **Creating IDs.** New IDs come only from `New()`, which produces UUIDv7: time-ordered and
  index-friendly. `Guid.NewGuid()` is banned.
- **Rebuilding IDs.** `From(Guid)` is used only at the edges: route values and storage.
- **Raw values.** Use `.Value` wherever a raw value is needed. Never rely on `ToString()`.
- **Cross-module references.** Other modules store foreign IDs as typed values, never as
  foreign keys.

## 8. Value types and validation

Parse, don't validate. Raw input becomes a value type once, at the start of a feature. After
that, the type guarantees validity.

```csharp
internal sealed record DisplayName
{
    public const int MaxLength = 100;
    public string Value { get; }

    private DisplayName(string value)
    {
        Value = value;
    }

    public static Result<DisplayName> Parse(string? input)
    {
        string trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxLength)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxLength} characters.");
            return Result<DisplayName>.Fail(Error.Validation("displayName", message));
        }

        return Result<DisplayName>.Ok(new DisplayName(trimmed));
    }
}
```

```csharp
// Data/DisplayNameConverter.cs: stored values go back through Parse, so invalid data fails loudly
internal sealed class DisplayNameConverter() : ValueConverter<DisplayName, string>(
    displayName => displayName.Value,
    value => DisplayName.Parse(value).Value);
```

- **When to create one.** A value gets a type when it has a rule: format, length, range, or
  normalization. Values without rules stay primitives.
- **Limits are written once.** The type's constants are reused by the EF registration. Never
  repeat a limit as a literal.
- **Several inputs.** Parse all of them, then combine into one validation error with every
  field:
```csharp
  Result<DisplayName> name = DisplayName.Parse(command.DisplayName);
  Result<EmailAddress> email = EmailAddress.Parse(command.Email);
  Result inputs = Result.Combine(name, email);
  if (inputs.IsError)
      return inputs;
```
- **Never silently truncate or "fix" input.** Reject it with a clear message. If shortening is a
  product rule, make it a named operation on its own type.
- **Field names.** Validation errors use the camelCase name of the input field.
- **Visibility.** Value types are `internal` unless another module must construct them; then
  they move to Contracts.

## 9. Entities

```csharp
internal sealed class User
{
    public UserId Id { get; private set; }
    public DisplayName DisplayName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    // Used by Register and by EF: parameter names match property names.
    private User(UserId id, DisplayName displayName, DateTimeOffset createdAt)
    {
        Id = id;
        DisplayName = displayName;
        CreatedAt = createdAt;
    }

    public static User Register(DisplayName displayName, TimeProvider time, IOutbox outbox)
    {
        User user = new User(UserId.New(), displayName, time.GetUtcNow());
        outbox.Add(new UserRegistered(user.Id, user.CreatedAt));
        return user;
    }

    public Result Rename(DisplayName displayName, IOutbox outbox)
    {
        if (DeactivatedAt is not null)
            return Result.Fail(UsersErrors.Deactivated);

        DisplayName = displayName;
        outbox.Add(new UserRenamed(Id, displayName.Value));
        return Result.Ok();
    }
}
```

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

## 10. Errors and results

```csharp
public static class UsersErrors
{
    public static readonly Error NotFound = Error.NotFound("users.not_found", "User not found.");
    public static readonly Error Deactivated = Error.Conflict("users.deactivated", "User is deactivated.");
}
```

```csharp
Result success = Result.Ok();
Result failure = Result.Fail(UsersErrors.NotFound);
Result<UserProfile> found = Result<UserProfile>.Ok(profile);
Result<UserProfile> missing = Result<UserProfile>.Fail(UsersErrors.NotFound);

if (found.IsError)
    return Result.Fail(found.Error);
UserProfile value = found.Value; // throws if found is an error: always check IsError first
```

- **Named factories only.** Results are created only through `Ok` and `Fail`. There are no
  implicit conversions.
- **Values vs exceptions.** Expected outcomes (invalid input, not found, conflict, forbidden)
  are `Error` values. Exceptions are for bugs and infrastructure failures.
- **What an `Error` carries.** A stable `Code`, a developer-facing English `Message`, a `Kind`,
  and field errors for validation. Clients localize by `Code`, never by `Message`.
- **Where errors are declared.**
  - Errors callers may branch on are declared once, in Contracts, as `<Module>Errors`.
  - Generic errors (`Error.Forbidden`, `Error.Validation(...)`) come from Platform.
- **HTTP mapping.** The gateway maps kinds to HTTP in one place (`Company.Platform.Web`):

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

## 11. Actor and authorization

- **Every contract method takes an `Actor`.** It is one of `Actor.ForUser(userId)`,
  `Actor.ForSystem("<module>.<process>")`, or `Actor.Anonymous`.
- **The gateway authenticates; modules authorize.**
  - The gateway creates the actor (`principal.ToActor()`) and requires authentication by default.
  - It decides no other permissions.
- **The owner of the data owns the permission rule.** That module checks the rule first. Other
  modules ask it.
- **Identity, not permissions.** The actor carries who is calling, never what they may do.
- **Passing actors along.**
  - Calls between modules on behalf of a user pass that user's actor along.
  - Reactions and jobs use a named system actor. Grep `ForSystem` to find every one of them.
- **Logging an actor.** `actor.ToLogValue()` returns `user:<id>`, `system:<name>`, or
  `anonymous`. It is the only way an actor appears in logs.

## 12. Persistence

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

- **One DbContext per module.** Each module has one `internal` `<Module>DbContext`, with its own
  schema and its migrations history table in that schema.
- **Everything is listed.** Every entity configuration and every type conversion is a visible
  line in the DbContext. No scanning.
- **Writes.**
  - Load the entity, call its method, then `await db.SaveAsync(ct)` once per feature.
  - `SaveAsync` (Platform) returns `Result` and turns concurrency conflicts into `Conflict`.
  - Outbox rows live in the same DbContext, so the change and its events commit together.
- **Reads.** Project straight to contract DTOs:
```csharp
  public async Task<Result<UserProfile>> GetProfileAsync(Actor actor, UserId id, CancellationToken ct)
  {
      if (!actor.Is(id))
          return Result<UserProfile>.Fail(UsersErrors.NotFound); // don't reveal that other profiles exist

      UserProfile? profile = await db.Users
          .Where(u => u.Id == id)
          .Select(u => new UserProfile(u.Id, u.DisplayName.Value, u.CreatedAt))
          .SingleOrDefaultAsync(ct);

      if (profile is null)
          return Result<UserProfile>.Fail(UsersErrors.NotFound);

      return Result<UserProfile>.Ok(profile);
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
- **Concurrency.** Entities that can be edited concurrently get a concurrency token in their
  configuration.

## 13. Migrations

- **One migration set per module.** Each module keeps its migrations in `Data/Migrations`,
  created against its own DbContext.
- **Applied migrations are history.** Never edit a migration that has been applied anywhere
  shared.
- **Snapshot conflicts.** Never hand-edit the model snapshot. After a merge conflict in it,
  remove your migration, rebase, and regenerate.
- **Applying them.**
  - Production applies migrations as a deployment step (an EF migration bundle or an idempotent
    script), never on startup.
  - Development may migrate on startup.
- **Destructive changes use expand–contract across releases.** Add the new shape, migrate the
  data, switch the code, and remove the old shape in a later release.

## 14. Integration events and reactions

```csharp
// Users.Contracts: a fact, named in the past tense
public sealed record UserRegistered(UserId UserId, DateTimeOffset OccurredAt);
```

```csharp
// Billing module: Reactions/OnUserRegistered.cs
internal sealed class OnUserRegistered(BillingDbContext db) : IReaction<UserRegistered>
{
    public async Task<Result> HandleAsync(UserRegistered @event, CancellationToken ct)
    {
        bool alreadyHandled = await db.Customers.AnyAsync(existing => existing.UserId == @event.UserId, ct);
        if (alreadyHandled)
            return Result.Ok(); // delivery is at-least-once

        Customer customer = Customer.CreateFor(@event.UserId);
        db.Customers.Add(customer);
        return await db.SaveAsync(ct);
    }
}
```

- **Event shape.**
  - Events are immutable records in the publisher's Contracts, named `<Noun><PastTenseVerb>`.
  - They carry IDs plus the facts consumers commonly need.
- **Who emits.** Only entities add events, through the `IOutbox` they receive as a parameter.
  The rows commit with the change.
- **Delivery and idempotency.** After commit, Platform's dispatcher delivers events at least
  once. Every reaction is idempotent, preferably check-then-act on a key protected by a unique
  index.
- **Failures.** A reaction that fails is retried with backoff. After repeated failures it is
  parked and logged for an operator.
- **Reaction shape.**
  - One reaction per file in `Reactions/`, named `On<Event>`, registered explicitly in
    `<Module>Module`.
  - A reaction commits only its own module's data.
- **Changing events.** Changes are additive. A breaking change is a new event type, and the old
  one keeps being published until no reaction uses it.
- **Events are announcements, not requests.** Use them for "something happened." To get an
  answer, call a contract.

## 15. Calling other modules

```csharp
// Organizations module: Features/AddMember.cs
internal sealed partial class OrganizationsApi
{
    public async Task<Result> AddMemberAsync(Actor actor, AddMember command, CancellationToken ct)
    {
        // Ask: awaited, read-only
        UserSummary? user = await users.FindAsync(actor, command.UserId, ct);
        if (user is null)
            return Result.Fail(OrganizationsErrors.UnknownUser);

        Organization? organization = await db.LoadOrganizationAsync(command.OrganizationId, ct);
        if (organization is null)
            return Result.Fail(OrganizationsErrors.NotFound);

        // Decide on our own data only; the entity checks the actor's role
        Result added = organization.AddMember(user.Id, actor, db.Outbox);
        if (added.IsError)
            return added;

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

## 16. Gateway endpoints

```csharp
// Company.Product.WebApi/Endpoints/UsersEndpoints.cs
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

- **One file per module.** Each module gets `Endpoints/<Module>Endpoints.cs`, with named handler
  methods, explicit binding attributes, and written return types.
- **Endpoints translate only.** Build the actor, map the request to a command, call the
  contract, and map the `Result` to HTTP. No logic, no DbContext, no direct Sdk calls.
- **Request records** live in the endpoints file and use the same field names as the command
  they map to.
- **Responses.** Return contract DTOs directly. Add a gateway response record only when the
  client needs a different shape.
- **Curate.** Not every contract method gets a route.
- **Routes.** Use plural kebab-case resources with IDs as segments. State changes that aren't
  plain updates become sub-resources (`POST /orders/{id}/cancel`).
- **Authentication is required by default** through the fallback policy. Anonymous endpoints
  say `AllowAnonymous()` explicitly.
- **JSON.** The gateway configures JSON from `PlatformJson.Options`: camelCase, enums as
  strings, strict number handling.
- **Screens.** A response that combines several modules lives in `Composition/`. Inbound
  webhooks verify their signature with the Sdk client, then call the owning module with a
  system actor.
- **OpenAPI.** The document is generated by ASP.NET Core's built-in OpenAPI support from the
  typed results.

## 17. Pagination

```csharp
public sealed record PageRequest(string? Cursor, int Limit);
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);
```

- **Keyset pagination with an opaque cursor.** Order by a unique, stable key; UUIDv7 IDs already
  order by creation.
- **Bounded size.** Platform clamps `Limit` to a maximum of 200. The gateway supplies the
  default page size explicitly.
- **No offset pagination,** and no total counts unless the product needs them.

## 18. Time

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

## 19. Configuration

```csharp
services.AddOptions<UsersOptions>()
    .BindConfiguration("Modules:Users")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

- **One options class per owner.** Modules bind `Modules:<Module>`; Sdk clients bind
  `Sdk:<Vendor>`. Options are bound and validated at startup.
- **Options only.** Read configuration only through options. Never inject `IConfiguration` into
  features.
- **Number format.** Configuration binds with the invariant culture, so decimal values in
  configuration always use `.` as the separator.
- **Secrets never go in committed files.** Use user-secrets locally, and environment variables
  or a secret store when deployed.
- **Prefer a sensible default to a setting.** Add a setting only for values that genuinely
  differ between environments.

## 20. Logging and telemetry

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

- **One catalog per module.** Each module declares its log messages in `Log.cs` as
  source-generated `[LoggerMessage]` methods.
- **Primitive parameters only.**
  - Allowed types: `string`, `bool`, integer types, `decimal`, `Guid`, `DateTimeOffset`,
    `TimeSpan`.
  - Complex types and enums are converted at the call site with a call you can see.
  - An architecture test enforces this.
- **What never goes in logs.** Log IDs only: never personal data, secrets, or tokens.
- **What to log.** Log what operators need. Expected errors aren't logged as errors, and
  unhandled exceptions are logged once.
- **Traces and metrics.**
  - OpenTelemetry is configured in the gateway.
  - A module adds an `ActivitySource` or `Meter` named `Company.Product.<Module>` only when it
    has something specific to measure.

## 21. External APIs

- **Official first.** Prefer the vendor's official .NET SDK. Otherwise, write a client in
  `src/Sdk` following `src/Sdk/README.md`.
- **Vendors stay inside modules.** A vendor is used from a module's internals.
- **Widely used vendors get one owner.** Prefer a module that reacts to existing events over one
  that everyone has to call.

## 22. Background work

- **Ownership.** Background work is owned by a module, lives in `Jobs/`, and is registered
  explicitly in `<Module>Module`.
- **Shape.**
  - Implement it as a `BackgroundService` driven by a `PeriodicTimer`.
  - Create one DI scope per iteration, and pass `stoppingToken` everywhere.
- **Bounded batches.** Each iteration processes a limited batch.
- **Assume several instances run the same job.** Work must be idempotent, and items are claimed
  atomically.
- **Actor.** Jobs use a named system actor when calling contracts.
- **Schedulers.** If you need cron schedules or durable job queues, choose one library, record
  it here, and use it everywhere.

## 23. Caching

- **No cache without a measured need.**
- **When there is one,** use `HybridCache`, only inside the module that owns the data. That
  module invalidates the cache whenever it writes.
- **Keys** follow `<module>:<entity>:<id>[:<variant>]`, built with
  `string.Create(CultureInfo.InvariantCulture, ...)`.
- **Permission-dependent results** are never cached under a key that doesn't include the actor.

## 24. Money (if the product handles money)

- **One type.** A single `Money` value type in `Company.Platform`: a `decimal` amount plus an
  ISO 4217 currency. Never `double` or `float`.
- **Rules live on `Money`.** Arithmetic across currencies fails, and rounding rules are defined
  on the type.
- **Storage.** Money is stored as two columns: amount and currency.
- **Formatting for humans uses an explicit culture.** Symbol placement and separators are
  culture data, never code.

## 25. Testing

- **Module tests** (`Company.Product.<Module>.Tests`):
  - They test features only through `I<Module>Api`.
  - They run against a real database (Testcontainers), reset between tests with Respawn.
  - When a module asks other modules, register those real modules too.
- **Coverage per feature.** The normal path, the consequential failure, and authorization.
- **Unit tests.** Value types and entities with real rules get unit tests. Entities receive a
  `RecordingOutbox`, and tests assert the events they added.
- **Gateway tests** use `WebApplicationFactory`. They cover routing, authentication,
  error-to-HTTP mapping, serialization, and a few end-to-end flows.
- **Architecture tests** enforce `ARCHITECTURE.md`.
- **Culture.** Every test project runs under `tr-TR` through one shared file, so culture bugs
  fail on every machine:
```csharp
  // tests/Shared/TestCulture.cs, linked into every test project by tests/Directory.Build.props
  internal static class TestCulture
  {
      [ModuleInitializer]
      internal static void UseUnfriendlyCulture()
      {
          CultureInfo culture = CultureInfo.GetCultureInfo("tr-TR");
          CultureInfo.DefaultThreadCurrentCulture = culture;
          CultureInfo.DefaultThreadCurrentUICulture = culture;
      }
  }
```
- **Fakes.**
  - Use `FakeTimeProvider` for time.
  - Fake only external seams; Sdk clients get a fake `HttpMessageHandler`.
  - Never mock the DbContext.
- **Test names state behavior,** for example `Rename_fails_for_deactivated_user`.
- **One framework.** xUnit, with one assertion style.

## 26. Build, analyzers, banned APIs

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" PrivateAssets="all" />
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />
  </ItemGroup>
</Project>
```

`tests/Directory.Build.props`:

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
  <ItemGroup>
    <Compile Include="$(MSBuildThisFileDirectory)Shared/TestCulture.cs" Link="TestCulture.cs" />
  </ItemGroup>
</Project>
```

`Directory.Packages.props` sets `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`,
so every package has exactly one version across the solution.

`.editorconfig`:

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true

[*.cs]
# Types are written out: no var, no target-typed new
csharp_style_var_for_built_in_types = false
csharp_style_var_when_type_is_apparent = false
csharp_style_var_elsewhere = false
csharp_style_implicit_object_creation_when_type_is_apparent = false
dotnet_diagnostic.IDE0008.severity = error

# Culture and string comparison must be explicit
dotnet_diagnostic.CA1304.severity = error
dotnet_diagnostic.CA1305.severity = error
dotnet_diagnostic.CA1307.severity = error
dotnet_diagnostic.CA1309.severity = error
dotnet_diagnostic.CA1310.severity = error
dotnet_diagnostic.CA1311.severity = error

[tests/**/*.cs]
# TestCulture.cs uses a module initializer on purpose
dotnet_diagnostic.CA2255.severity = none
```

`.gitattributes`:

```
* text=auto eol=lf
```

`BannedSymbols.txt`:

```
P:System.DateTime.Now;Use TimeProvider
P:System.DateTime.UtcNow;Use TimeProvider
P:System.DateTime.Today;Use TimeProvider and an explicit time zone
P:System.DateTimeOffset.Now;Use TimeProvider
P:System.DateTimeOffset.UtcNow;Use TimeProvider
P:System.TimeZoneInfo.Local;Use an explicit time zone
M:System.DateTime.ToLocalTime;Use an explicit time zone
M:System.DateTimeOffset.ToLocalTime;Use an explicit time zone
P:System.Globalization.CultureInfo.CurrentCulture;Pass a CultureInfo explicitly
P:System.Globalization.CultureInfo.CurrentUICulture;Pass a CultureInfo explicitly
M:System.Guid.NewGuid;Use <TypedId>.New()
```

- **Noisy analyzer rules** are tuned in `.editorconfig`, never with `#pragma` scattered through
  the code.
- **New packages need clear net value.** Call them out in the change summary.
- **Repeated mistakes become bans.** When a mistake repeats, ban the API or raise an analyzer
  rule rather than adding another paragraph here.

## 27. Recipes

### Add a feature

1. Add the method, with its command and DTO records, to `I<Module>Api`.
2. Implement it in `Features/<FeatureName>.cs`, following the five-step shape.
3. Put any new rule in a value type or entity. Methods that emit events take `IOutbox`. Add
   errors callers may branch on to `<Module>Errors`.
4. If clients need it, add a named handler in `Endpoints/<Module>Endpoints.cs`.
5. Test it through the contract: normal path, consequential failure, authorization.
6. Update the module README if the contract's purpose, events, or dependencies changed.

### Add a module

0. **Check first.** Should an existing module own this capability?
1. **Create the two projects.**
   - `Company.Product.<Module>.Contracts` references `Company.Platform`.
   - `Company.Product.<Module>` references its Contracts and `Company.Platform.Modules`, and
     declares `InternalsVisibleTo` for its test project.
2. **Add the standard files:**
   - `<Module>Module`.
   - `<Module>Api` (the partial root).
   - `<Module>DbContext`, with its schema, its outbox, and every configuration and conversion
     listed.
   - `Log.cs`.
3. **Write its map.** Copy `docs/templates/module-readme.md` into the module folder and fill it in.
4. **Wire it up.** Register it in `Program.cs`, and add an endpoints file if clients need one.
5. **Update the shared docs.** Add the module to the Module map in `ARCHITECTURE.md`, and its
   terms to `GLOSSARY.md`.
6. **Create the initial migration.**

### Add an external API client

Follow `src/Sdk/README.md`.
