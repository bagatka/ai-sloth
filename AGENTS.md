# How we build software

## The goal

Build useful products whose entire structure one person can hold in their head.

Not every line — every level. A system is made of a few parts, each understandable through its contract alone, and each part is built the same way. At any level, a person or agent should be able to understand, change, or fix that level by reading it plus the contracts directly beneath it, never by reading everything. Complexity may grow in depth. It may not grow in what must be understood at once.

This also defines how humans and agents share work. Humans own the map and review every contract change. Implementation inside a contract can be delegated and is verified by that contract's tests.

Correctness, security, privacy, data integrity, and required compatibility are constraints, not trade-offs. Reduce scope before weakening them.

## 1. Build systems out of systems

A system is a small number of parts composed through explicit contracts. Each part is itself a system built the same way. There is no separate "architecture level" and "code level"; there are only levels.

**Each level reads in its own vocabulary.** Code at one level describes *what happens* in terms of the capabilities directly beneath it, not *how* they work.

```ts
// Product level: reads as a description of the workflow.
// (Dependencies are provided by the composition root; language is illustrative.)
async function publishPost(author: UserId, draft: Draft): Promise<PublishResult> {
  const media = await mediaLibrary.finalize(draft.mediaIds);
  const post  = await posts.create({ author, caption: draft.caption, media });
  await feed.distribute(post.id);
  return { postId: post.id };
}
```

Understanding this function requires the contracts of `mediaLibrary`, `posts`, and `feed`, not their implementations. Inside `feed`, the same holds one level down: fan-out, ranking, and storage are each their own capability behind their own contract. This level still owns what only it can know. For example, it must decide what "published" means if distribution fails after the post exists. Failure semantics that span several capabilities belong to the level that composes them.

Rules:

- **Don't mix levels in one unit of code.** A function that calls `feed.distribute()` and also builds SQL strings is doing two levels' work.
- **Keep each level small.** If a level composes more than about seven direct parts, look for a real concept that groups some of them. A folder is not enough; the grouping needs its own contract.
- **Every boundary must be deep.** It should hide substantially more than it exposes. A layer that only forwards calls, renames things, or wraps a stable platform type is not a level. It is noise that every reader must walk through. Recursive decomposition only works if every boundary earns its place.
- **Split for real reasons.** Split when parts hide different decisions, change for different reasons, or can be understood and verified independently. Keep parts together when splitting would require shared internals, chatty back-and-forth, or an invariant spanning both sides. Never split by file size, execution step, or fashion.
- **Dependencies point down.** A part depends on the contracts of what it uses. It never knows its parent and never reaches into a sibling's internals. Wiring happens in the parent, the composition root. There are no cycles; a cycle means ownership is wrong.
- **One owner per rule and per piece of mutable state.** Others ask the owner. They do not re-implement its rules or mutate its state.
- **Prefer in-process boundaries.** A network boundary is a contract boundary plus latency, partial failure, and operational cost. Create one only when scaling, deployment, isolation, or ownership by another team requires it. Good in-process contracts make that move possible later without redesign.

**Holdability signals.** These detect a level that has outgrown a head. They are signals, not quotas.

- The level can be explained in about five sentences using only its own vocabulary.
- Its contract fits on roughly one screen.
- Changing it requires reading it plus the contracts of its direct dependencies, and nothing deeper.
- Typical changes stay inside one part. If changes routinely ripple across several parts (visible in version history as repeated co-changes), a boundary is in the wrong place.
- If you must read a dependency's implementation to use it correctly, that dependency's contract is defective. Fix or report the contract.

## 2. Contracts are the design

Design is a conversation about data types and contracts. Implementations are replaceable details behind them.

**Data first.** For a new or changed boundary, define the data types and write a representative caller before writing the implementation. Show the normal case and one consequential failure. Redesign the contract, not the caller, if the caller has to do any of these:

- understand storage,
- reconstruct policy,
- coordinate hidden state,
- perform a fragile sequence of calls.

**A contract is behavior, not a signature.** Where it matters at that boundary, state:

- **Meaning:** what it does, its inputs and outputs, and its guarantees.
- **Validity:** preconditions, invariants, and what absence or rejection looks like.
- **Effects:** state changes, I/O, ownership, and lifetime.
- **Failure:** what each error means, whether partial completion is possible, and what the caller must do to recover.
- **Limits:** ordering, concurrency, idempotency, cancellation, cost, and the performance characteristics callers depend on.

**Hide implementation, never operational meaning.** A caller need not know the schema. It must know that an operation persists data, may partially succeed, or performs expensive I/O. Never promise atomicity, durability, or safe retries the implementation doesn't provide.

**Plain data across boundaries.** Prefer immutable values and explicit identifiers over live objects that carry hidden state or callbacks into the caller's world. Plain data is easier to reason about, test, serialize, and later move across a process boundary. Make invalid states unrepresentable where the type system allows it.

**One authoritative contract, enforced by the language.** The contract lives in one place, next to its implementation. It is expressed with the language's types, visibility, and concise docs. Everything not in the contract is private, enforced by the compiler or a dependency rule rather than by convention. Don't write duplicate contract documents for ceremony.

**Conformance tests make swapping real.** Some contracts have several implementations or may be replaced; write tests for those against the contract, not the implementation. Every implementation, including test fakes, must pass the same suite. These tests are what allow a module to be rewritten without its callers noticing.

**Anything observable will be depended on.** Callers rely on whatever behavior they can see, documented or not: ordering, timing, error text, defaults. Keep the observable surface small and deliberate. Treat changes to observable behavior as contract changes, even when signatures stay the same.

**Contract changes are design changes.** Implementation changes inside a contract are local and cheap. Contract changes are expensive and must be explicit about:

- what changes and why,
- every affected caller,
- every affected persisted or external representation,
- the migration.

Prefer additive changes. Never change a contract silently as a side effect of other work.

## 3. General-purpose capabilities, thin product glue

Build subsystems as if they will serve a product you haven't written yet. The product-specific part of the system should be a thin layer of composition on top of general capabilities.

- **A capability doesn't know which product it lives in.** Product names, screens, campaigns, and business flows stay out of its vocabulary: `MediaLibrary`, not `StoryPhotoUploadManager`.
- **Product decisions live in the composition layer.** That covers which capabilities run, in what order, and under which policies. Glue code is expected; it is the cheapest code to understand and change.
- **Keep each invariant with the module that can enforce it.** Generality must not push hard decisions onto every caller.
- **Use the extraction litmus test.** Could this module move into its own package and serve a different app with only new glue? Would its contract still make sense there? Don't actually extract or publish it until a real need exists. Internal reuse is not a public compatibility promise.

**General is not the same as configurable.** The right generalization finds a more fundamental concept, and the contract usually gets *smaller*. For example, one "media item" replaces photo, photo-with-song, and photo-with-voiceover. The wrong generalization adds flags, modes, option bags, plugin hooks, and extension points for imagined callers. The contract grows, and every caller must understand more. If generalizing increases the number of concepts a caller must learn, it is the wrong generalization.

Be general in concept and minimal in surface: model the general concept, and implement only the parts of it needed now.

## 4. One problem, one solution

Similar problems are solved the same way everywhere. Two ways of doing the same thing is worse than one imperfect way. Everyone has to learn both, and every reader has to work out whether the difference matters.

- **Recurring problems have one established solution,** recorded in the pattern registry. Typical entries:
  - errors, validation, and IDs
  - time and time zones, and money
  - configuration, and logging and telemetry
  - authentication and authorization
  - persistence access, retries and timeouts, pagination, and cancellation
  - UI state, forms, and navigation
  - naming and test structure
- **Registry entries are short.** Each states the problem, the chosen solution, and the path to a canonical example in the code. The example is the specification.
- **Follow what exists.** Before solving a recurring problem, find the established solution and follow the shape of its canonical example.
- **Fill gaps once.** If no solution exists, choose deliberately, implement it once and well, and add it to the registry in the same change.
- **Change patterns, don't fork them.** If the established solution is wrong for a case, propose changing the pattern, with a migration path for existing uses. Never introduce a second way "just here."
- **One vocabulary.** Each domain concept has one name, listed in the glossary and used identically in code, APIs, storage, UI, and conversation. Don't introduce synonyms or reuse a name for a different concept.
- **Uniform shape.** Every subsystem looks the same from the outside: the same place for its contract, its README, and its tests. People know where to look before they've seen it.
- **Across stacks, keep concepts consistent and expression idiomatic.** The same error taxonomy, contract style, and domain vocabulary apply in every language, using each platform's native mechanisms. Don't fight the platform to make code look identical.

## 5. Keep the map

The map is what lets one person hold the system.

- Every repository has a system map. It lists the top-level subsystems, a one-line purpose for each, the dependency direction, and where each contract lives. It fits on one page.
- Every substantial subsystem has its own short map, a README at its boundary, with the same shape. Maps nest the way systems nest.
- Any change that adds, removes, splits, merges, or re-wires a subsystem updates the affected map in the same change.
- Maps describe structure and purpose. They don't catalogue files or duplicate contracts.

## 6. Simple for the user

The product is the outermost system, and its contract is with the user. The same rule applies: the user should be able to hold the product in their head.

- **Prefer a few powerful concepts that compose over many narrow features.** Every new product concept must coexist with every existing one, so its cost to users grows with the product, not with the diff.
- **Prefer a coherent default over configuration.** Add a choice when distinct real needs require it, not to avoid making a decision.
- **Never expose internal structure** as steps the user must coordinate.
- **Treat the edges as behavior, not polish.** That includes first use, feedback, empty states, errors, recovery, and accessibility.
- **Make the experience concrete before choosing an architecture.** Describe the normal workflow and one important failure path: a UI sketch, a CLI invocation with its output, or a caller example for a library.

## 7. Solve the problem, not the ticket

Act as a technical partner, not an order taker. Requests often describe a narrow solution, or a pre-cut version of one, without knowing what it costs or what a slightly different shape would make possible. Find the version that delivers the most value for the least lasting complexity.

For any new feature or significant behavior change:

1. **Restate the underlying problem.** Who is trying to do what, and what makes it hard today? What observable result would make this worth doing?
2. **Check what exists.** Could an existing capability, a better default, or removing friction solve it without new machinery?
3. **Look for the general case.** Is this a special case of a more fundamental capability? Would building that capability cost about the same or less, remove special cases, or clearly serve where the product is going?
4. **Price the integration, not just the feature.** Count:
   - new concepts, for users and for code,
   - contracts that must change,
   - interactions with existing features,
   - special cases added,
   - ongoing operational cost.

   A feature that is cheap to build but touches everything is expensive.
5. **Watch for cuts that add code.** Restrictions like "only for these users", "only on this screen", "max three", or "not in this mode" are each a condition that must be implemented, tested, explained, and eventually removed. A restricted version is often more code and more concepts than the unrestricted one. Say so.
6. **Recommend.** Present the meaningful options: as requested, the general version, a smaller version, or not building it. Give each one's cost, risk, and what it enables. Recommend one and explain why.

Example: a photo-sharing product asks for background music on photo posts. The underlying need is expressive posts with sound.

- Photo-plus-audio adds a hybrid content type, audio sync, and likely licensing and streaming integrations.
- First-class video may cover the same need, since users can post a photo set to music as a video. It adds one general concept instead of a special case, and it enables far more.
- Video has real costs of its own (encoding, storage, bandwidth), so the recommendation depends on actual numbers. Analyze; don't pattern-match.

Guardrails:

- **The human decides product outcomes.** Never silently build something different from what was asked. If a different shape seems better, say so before building.
- **Generalization is not scope creep.** Propose the bigger capability only when it removes special cases or costs about the same. Name the smallest first slice of it that delivers the requested value.
- **"Don't build this" is a legitimate recommendation** when cost clearly exceeds value or the feature would make the product incoherent. Make the case with specifics.
- **Keep it proportional.** A clear, small, local request gets done, not debated.

## 8. How to work here

**Read by level.** When changing a module, read:

- its map and its contract,
- its implementation,
- the contracts (not implementations) of its direct dependencies,
- its direct callers, when the contract is involved.

Descend into a dependency's implementation only when evidence points there, and then treat it as working in that module. Don't inventory the whole repository.

**Scale design effort to risk, not diff size.**

- **Change within an established contract:** understand the contract, make the change, verify it. No design document.
- **New capability, unclear behavior, or a changed boundary:** before implementing, state the problem, the scope, a contract sketch with a caller example, and how you will verify it. A few paragraphs are enough.
- **High-risk or hard-to-reverse changes,** even with small diffs: this covers permissions, persisted meaning, shared contracts, and irreversible operations.
  - Compare meaningfully different approaches, including the simplest direct one.
  - Examine failure modes and compatibility.
  - Run a focused spike when an assumption is cheaper to test than to debate.

**Ask only when it matters.** Ask when missing information would materially change the outcome or its safety. Otherwise, state the simplest reasonable assumption and proceed.

**Rewrite inside a boundary when that is simpler.** Implementations are swappable, so rewriting a module's internals against its contract and conformance tests is legitimate when it is in scope and clearly simpler than patching. This is not permission to rewrite unrelated code.

**Build in working slices.** Connect pieces into a real end-to-end workflow early, rather than finishing isolated subsystems before learning whether they compose.

- Keep contracts provisional while exploring, explicit while integrating, and managed once callers depend on them.
- Revise an awkward boundary instead of accumulating workarounds around it.
- Keep unfinished or simulated behavior out of production paths.

**Delegate along boundaries.** Agree on shared contracts before parallel work starts. Give each worker:

- the contract it implements,
- the contracts it may use,
- its permitted scope,
- its acceptance checks.

Never let parallel workers each invent a shared contract. Integrate and verify the composed behavior yourself; locally passing parts are not enough.

**Stay in scope.** Make the smallest coherent change, including the narrow boundary repair needed for correctness.

- No unrelated refactors, speculative infrastructure, or formatting churn.
- Preserve others' work.
- Remove paths your change made obsolete.

## 9. The engineering floor

These hold at every level, whatever the scope.

- **Failure honesty.** Validate untrusted input at trust boundaries, and represent expected absence explicitly. Preserve error context. Never hide failure behind invented data, silent fallbacks, or weakened checks. Retries and compensation must be explicit and semantically safe.
- **Proportional handling.** Effort follows how often a case happens and what it costs when it
  does. Anything users will plausibly hit, about one operation in a hundred or more often, is
  handled properly. A rarer case gets its own handling only when that takes a few lines; otherwise
  it fails through the general path, with an error that says what is off and a `// Not handled:`
  comment naming the case and what handling it would take. A small share of the code should cover
  nearly all cases. Rarity never excuses silent failure, corrupted data, or a security hole.
- **Security and privacy.** Use least privilege and safe defaults. Keep secrets and sensitive data out of logs, errors, and fixtures. Irreversible operations require appropriate authorization and a recovery strategy.
- **State and resources.** Every piece of mutable state and every resource has an obvious owner and lifetime.
  - Persistence, transactions, partial completion, and cleanup are deliberate.
  - Propagate cancellation and deadlines.
  - Bound work, memory, queues, retries, and concurrency.
  - No unowned background work.
- **Performance is decided at contracts.** Choose algorithms and data representations for expected scale.
  - On hot paths, design contracts around batches and plain data ("process these 10,000 items") rather than chatty per-item calls. A boundary must not impose a cost its implementation can't remove.
  - Measure real workloads before adding caches, queues, or concurrency. Each one needs a demonstrated purpose and defined consistency, failure, and resource semantics.
- **Dependencies.** Prefer mature platform and standard-library capabilities. Add a third-party dependency only for clear net value after weighing maintenance, security, and compatibility. Isolate it behind a boundary when its details would otherwise spread.
- **Code.** Use precise names, visible control flow, and local conventions. Comments explain intent, invariants, and non-obvious trade-offs, not syntax. Generate mechanical artifacts from a reviewable source, and never hand-edit generated output.
- **Direct calls, owned state.** Call an object's methods where the decision is made,
  `process.Kill()`, and handle failures at that call site. Don't wrap a single call in a helper that
  takes the object and returns nothing. A helper earns its place by computing a value or hiding real
  knowledge. Inside a module, objects own their state and change it through their own methods;
  immutable plain data is for boundaries and for sharing between threads.
- **Explicit over implicit.** Code says what it does where it does it: visible registration instead of scanning, named conversions instead of implicit ones, types written where they aren't obvious, defaults stated instead of inherited from the framework. A contributor who doesn't know the platform's conventions should still read any file correctly. An implicit mechanism is allowed only where forgetting the explicit version would be a correctness or security bug, and each one is recorded in the pattern registry.
- **Same behavior on every machine.** Never depend on the machine's culture, time zone, line endings, path separators, or file-name casing. Machine-readable text uses invariant formats; human-readable text uses a culture passed explicitly. Tests run under a deliberately unfriendly culture so these bugs fail everywhere, not only on a colleague's machine.

## 10. Verify and report

- **Test guarantees through boundaries.** Cover:
  - conformance tests for contracts,
  - a test of the composed path to catch mismatched assumptions,
  - the regression being fixed,
  - risk-relevant failures, limits, and state transitions.

  Prefer deterministic tests with realistic values and focused fakes at external seams. Don't couple tests to private structure.
- **Exercise the real outcome** where the environment allows: the actual UI, command output, or caller experience, including an important failure. State what you could not exercise.
- **Run the repository's checks.** Use visibility rules, dependency rules, and architecture tests to protect boundaries. Add a focused check when a consequential boundary needs protection.

Before finishing, ask:

- Does each level I touched still read in its own vocabulary and fit in a head?
- Can a caller use every changed contract without reading its implementation?
- Is each rule and piece of state owned once? Did I solve recurring problems the established way?
- Did I add a concept, layer, option, dependency, or special case? Does it remove more burden than it adds?
- Are the maps, pattern registry, and glossary current?
- What evidence supports the result, and what remains unverified?

Report the outcome, any contract changes (called out explicitly), the checks actually run and their results, and remaining risks. Keep the report proportional. Never claim a check passed that wasn't run.

## Translating to stacks

| Concept | .NET | React / TypeScript | SwiftUI | Zig |
|---|---|---|---|---|
| Boundary | Project / assembly | Feature package with one entry point | Swift package / SPM target | Module (`@import`, `build.zig`) |
| Contract | `public` interfaces, records, error types | Exports of the entry `index.ts`: types, hooks, props | `public` protocols and value types | `pub` declarations, their types and error sets |
| Hidden | `internal` | Unexported files; deep imports banned | `internal` / `package` / `private` | Non-`pub` declarations |
| Composition root | `Program.cs`, DI registration | App shell, routes, providers | `App` entry, root views | `main.zig`, build graph |
| Enforcement | Project references, architecture tests | Import-path lint rules, package `exports` | Target dependencies, access control | Module graph in `build.zig` |

For network services, the contract is a machine-readable schema (e.g. OpenAPI or protobuf). It is the single source, and client and server types are generated from it.

## Repository specifics

Local rules refine this document. Where they conflict, follow the local rule and mention the
conflict.

- **Stack:** .NET 11 RC1 (C# 15), ASP.NET Core minimal APIs, EF Core, PostgreSQL 18,
  OpenTelemetry, Aspire, xUnit v3 on Microsoft Testing Platform v2.
- **Environment:** run `./dev` (Nix) for the pinned SDK; the shell also pins `DOTNET_ROOT`.
- **Commands:**
  - Build: `dotnet build AiSloth.slnx`
  - Test: `dotnet test --solution AiSloth.slnx`
  - Format check: `dotnet format AiSloth.slnx --verify-no-changes`
  - Aspire CLI (pinned in `dotnet-tools.json`): `dotnet tool restore` once, then
    `dotnet aspire run` to run locally and `dotnet aspire update` to upgrade Aspire
  - New migration (verify once the first module exists):
    `dotnet ef migrations add <Name> --project src/ControlPlane/Modules/<Module>/Bagatka.AiSloth.<Module> --startup-project src/ControlPlane/Bagatka.AiSloth.WebApi --context <Module>DbContext --output-dir Data/Migrations`
- **System map:** `ARCHITECTURE.md`
- **Pattern registry:** `PATTERNS.md`
- **Glossary:** `GLOSSARY.md`
- **Module maps:** `src/ControlPlane/Modules/<Module>/README.md`, from the template
  `docs/templates/module-readme.md`
- **Contracts and enforcement:**
  - Each module's contract is `I<Module>Api` in `Bagatka.AiSloth.<Module>.Contracts`.
  - Everything else in a module is `internal`.
  - Boundaries are enforced by project references, `internal`, analyzers (`AnalysisMode` `All`,
    Meziantou, banned APIs), and `tests/Bagatka.AiSloth.ArchitectureTests`.
- **Known exceptions:** none yet.
