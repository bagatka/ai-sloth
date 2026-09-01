# Golden Engineering Standard

## Mission

Build the smallest system that fully satisfies the current contract, preserves critical invariants, is secure and bounded under realistic load, and stays obvious to understand, operate, and change.

Optimize lifetime complexity: **change amplification, cognitive load, unknown unknowns**. Not line count, initial coding speed, abstraction count, or hypothetical flexibility.

Authority: direct task > nearest scoped repository instruction > this file. Keep repository commands, architecture, supported versions, and stack rules in local guides. Apply silently; deliver working software and evidence, not ceremony.

Conflict order: correctness/security/privacy/data > explicit behavior/ownership/failure > simplicity/stable surface > measured performance/bounds > maintainability/operability > extensibility/elegance/brevity.

## Work

1. **Inspect before editing.** Read applicable instructions, task, callers, contracts, code, tests, docs, config, and relevant history. Verify version-sensitive choices against primary documentation for supported versions; do not guess when evidence exists.
2. **Frame the problem.** Define observable behavior and non-goals; identify invariants, trust boundaries, owners/lifetimes, side effects, failure semantics, compatibility/migration needs, expected scale, and hot paths. Separate facts from assumptions; surface conflicts. Ask only when ambiguity materially changes public behavior or a high-risk outcome; otherwise choose the simplest safe interpretation.
3. **Design deliberately.** For non-trivial or hard-to-reverse work, compare two materially different designs. Prefer lower total complexity, smaller blast radius, easier removal. State a non-obvious interface contract before implementing it; if hard to state simply, redesign. Follow local patterns unless they cause the problem.
4. **Change narrowly.** Make the smallest coherent end-to-end diff. No unrelated refactor, formatting churn, speculative scaffold, duplicate path, or hidden behavior. Preserve unrelated work; delete code made obsolete.
5. **Verify by risk.** Run focused checks first, broader checks when justified. Review the diff for scope, invariants, security, compatibility, public surface, I/O, allocation, synchronization, cleanup, and dead code.
6. **Report evidence.** State what changed, checks and results, checks not run, material assumptions, and known limits. No correctness or performance claim without evidence.

## Design Against Complexity

- **Deep modules:** small stable interface, substantial useful behavior. Avoid shallow wrappers and pass-through layers.
- **Hide knowledge:** each format, invariant, policy, and design decision has one authoritative home. Do not leak storage, transport, framework, vendor, or generated details across boundaries.
- **Make layers earn existence:** each layer must simplify, translate, or enforce policy. Organize around hidden knowledge, not execution order. Pull unavoidable complexity into the module best placed to own it.
- **Make APIs hard to misuse:** common path obvious; invalid states difficult to express when valuable; define avoidable errors out of existence. Prefer explicit dependencies and composition over globals, service locators, ambient context, or convenience inheritance.
- **Abstract on evidence:** current variation, stable boundary, or important invariant. A general core is useful only when it reduces complexity for current cases. One implementation justifies an interface only at a real boundary. Testing trivial code does not. Handle edge cases only when required, observed, plausible and high-impact, or cheap to support correctly; otherwise reject clearly.
- **Keep code obvious:** precise domain names, cohesive units, one-way dependencies, visible control flow/state/side effects/cost. Comments explain intent, invariant, policy, trade-off, or non-obvious cost—not syntax.
- **Minimize obligations:** every public symbol, option, event, protocol, persisted field, and extension point is durable compatibility surface. Expose only caller needs through project-owned contracts; avoid convenience API that adds no invariant or policy.

## Correctness, Safety, Resources

- **Ownership:** every mutable state, task, subscription, connection, transaction, cache, buffer, lock, temporary resource, and background operation has one obvious owner, defined lifetime, cleanup on every exit, and a bound when growth/fan-out is possible. No named owner: redesign.
- **Concurrency:** prefer scoped structured work; no unowned fire-and-forget. Propagate cancellation/deadlines; bound parallelism, queues, retries, batches, streams. Prefer immutable sharing or ownership transfer. Never hold synchronization across blocking, suspension, or unknown external work; revalidate after suspension. Document thread safety, ordering, reentrancy, idempotency, lifecycle when contractual.
- **Boundaries and security:** validate untrusted input at entry; normalize once. Use least privilege and safe defaults. Never expose secrets or sensitive payloads in logs, errors, diagnostics, examples, tests, or fixtures.
- **Failures:** expected absence is absence; invalid required state is failure. Preserve cause, context, cancellation, timeout. Translate only where stable policy exists; make caller action/retryability clear. Retries, fallback, deduplication, compensation, and idempotency must be explicit, bounded, semantically safe. Never swallow failure, invent required data, or silently weaken correctness/security.
- **Side effects:** make I/O, persistence, transactions, partial failure, and recovery visible. Never discard unrelated work or rewrite history. Destructive data or irreversible action requires explicit authorization and a recovery path.
- **Efficiency:** choose algorithms/data for expected scale; know non-trivial asymptotic cost. Remove work before optimizing. Avoid repeated work/I/O, N+1 access, chatty boundaries, redundant conversion/serialization, unnecessary allocation/copy, large materialization. Bound memory, output, concurrency, queues, caches, registries, retries, retained state. A cache needs owner, limit, consistency semantics, invalidation/eviction, cleanup, and evidence. Measure realistic before/after; account for latency, throughput, memory, code size, correctness, maintenance; protect stable hot paths with benchmarks or thresholds.

## Evidence and Evolution

- **Tests:** add the smallest deterministic set likely to catch a meaningful regression. For bugs, reproduce before fixing when practical. Cover normal path, regression, important boundary, representative failure; add concurrency/cancellation/retry/cleanup/migration/memory/performance tests only where behavior or risk exists.
- **Determinism:** control time, randomness, IDs, environment, locale, ordering, external I/O, scheduling. No arbitrary sleeps for synchronization.
- **Test seams:** use stable boundaries, real values, focused fakes at real seams, realistic external fixtures. Do not add production abstractions or broad mocks solely for trivial pass-through code. Coverage supports evidence; it is not the goal.
- **Checks:** run repository-mandated format, static analysis, build, and tests relevant to touched code. Validation strength matches failure cost. Never claim an unrun check passed.
- **Dependencies:** standard library/platform/repository first. Add only clear net value after transitive size, updates, compatibility, license, supply-chain, performance, and operational ownership. Avoid thin convenience dependencies; keep third-party types behind project boundaries; remove dependencies that stop paying.
- **Generated code and automation:** generate from one canonical reviewable source, reproducibly; never hand-edit output or let generated shapes dictate human-facing design. Add scripts/CI gates only for a repeated mistake or meaningful contract. Automation removes ceremony; it does not manufacture it.
- **Evolution:** honor support floor; prefer additive public/persisted changes. Breaking change requires explicit intent, impact analysis, migration/rollout guidance, rollback or recovery. Keep code, tests, docs, schemas, examples, migrations, and behavior consistent. Observability answers operational questions without noise/secrets and never becomes control flow.

## Complexity Gate

Before adding a type, layer, interface, dependency, task, cache, option, script, service, extension point, or optimization, answer: **what current requirement/invariant/measured problem/high-impact risk pays; who owns/calls/bounds/cleans it; why direct design fails; total cognitive/runtime/compatibility/test/maintenance cost; how behavior is proved; how it can be removed.** Weak or hypothetical answers: do not add it.

Reject by default: shallow/pass-through layers; duplicated knowledge; modules split by execution order while sharing the same information; vague `manager`/`helper`/`utility`/`service` buckets; hidden globals or implicit I/O/persistence/cache/retry/task/background work; unbounded resources/concurrency; speculative plugins/distribution/microservices; unrelated broad refactors; custom frameworks over direct platform primitives; unevidenced caches/optimizations/dependencies; fake tests, vanity coverage, unrealistic benchmarks.

## Done

A change is done when it:

- satisfies stated behavior and critical invariants
- is the smallest coherent solution: no unrelated scope, dead path, duplicate behavior, accidental public surface
- makes ownership, lifetime, side effects, failure, recovery, and bounds explicit
- keeps public/persisted contracts intentional, compatible, documented; includes migration when needed
- passes meaningful deterministic tests and relevant checks proportional to risk
- addresses security, privacy, performance, and operations where relevant
- reports evidence, assumptions, unrun checks, known limits

Default: inspect uncertainty; remove complexity; minimize surface; bound growth; expose failure; measure claims; apply rigor proportional to failure cost.


---


# Golden Engineering Standard 2

## Mission

Build the smallest system that fully satisfies known requirements, protects critical invariants, performs well under realistic load, and remains obvious to understand and change.

This is the always-loaded standard for human and automated contributors. It is independent of language, framework, domain, and tool. Explicit repository and product rules override it. Keep stack-specific detail in small companion guides loaded only when relevant; do not duplicate it here.

Apply the standard silently. Prefer working software and evidence over plans, essays, ceremony, and claims.

## Priority Order

When goals conflict, prefer:

1. Correctness, security, privacy, and data integrity.
2. Explicit behavior, ownership, lifetime, and failure semantics.
3. Simplicity, readability, and a small stable surface.
4. Measured performance and bounded resource use.
5. Maintainability, testability, and operability.
6. Extensibility, elegance, and local brevity.

Shorter is not simpler when it hides behavior. More comprehensive is not better when it serves imaginary requirements.

## How to Work

- Inspect relevant requirements, contracts, code, tests, documentation, configuration, and local patterns before editing. For version-sensitive decisions, use primary specifications or official documentation matching supported versions. Do not guess from memory when evidence is available.
- Identify the caller, required behavior, invariants, trust boundaries, ownership, failures, compatibility constraints, and likely hot paths.
- Separate facts from assumptions and surface material conflicts. Ambiguity is not permission to generalize: ask only when it materially changes the result; otherwise choose the simplest safe interpretation and state it.
- Make the smallest coherent end-to-end change. Keep unrelated refactors, formatting, infrastructure, and future-proofing out of the diff. Follow existing conventions unless they cause the problem.
- Keep control flow, state changes, side effects, cost, I/O, retries, caching, concurrency, and background work visible. Delete obsolete code; leave no parallel path or speculative scaffold.
- Validate in proportion to risk: focused checks first, broader checks when justified. Review the diff for unnecessary code, abstraction, allocation, I/O, synchronization, public surface, and scope.
- Report what changed, what was validated, material assumptions, and known limits. Make no correctness or performance claim without evidence.

## Make Complexity Earn Its Place

Complexity must pay rent through a current requirement, observed problem, measured bottleneck, real boundary or invariant, or concrete high-impact risk.

Prefer direct code and one obvious implementation. Add an abstraction only when it removes more complexity than it adds and protects real variation, a stable boundary, or an important invariant. Multiple real uses are evidence; one strong boundary can also be enough. Testing trivial code is not a reason to abstract it.

Do not support hypothetical edge cases by default. Support a case when it is required by contract, observed, plausible and high-impact, or cheap to handle correctly. Otherwise reject it explicitly and document the limit. Clear non-support is better than complex partial support. Allocate effort by contract, likelihood, impact, and reversibility not possibility alone.

Cut scope, not correctness. Security, permissions, privacy, money, data loss or corruption, concurrency safety, resource cleanup, public compatibility, and irreversible actions demand rigor even when failures are rare.

Optimize total complexity, not line count. Eighty clear lines are better than hundreds for imaginary flexibility; explicit code is better than compressed magic.

## Design Small, Honest Interfaces

- Model data and invariants so control flow stays simple. Make invalid states difficult to express when this materially improves correctness; avoid type ceremony for trivial local values.
- Use precise domain names. Keep units cohesive, interfaces small, dependencies one-way, and implementation details replaceable. Separate domain policy from transport, storage, UI, frameworks, generated code, and vendors.
- Treat every public symbol, option, extension point, and persisted format as a long-term obligation. Expose only what callers need through stable project-owned contracts. Do not leak incidental implementation or dependency types unless exposure is the product.
- Do not add public convenience that callers can express in one obvious line unless it protects an invariant or encodes policy.
- Prefer concrete implementations until substitution is real. Use interfaces, generics, plugins, reflection, metaprogramming, or code generation only for a demonstrated need.
- Prefer explicit dependencies and composition over hidden globals, service locators, ambient context, and inheritance for convenience. Make the common path obvious and hard to misuse.

## Make Ownership and Lifetime Obvious

Every mutable state, task, subscription, connection, transaction, cache, buffer, lock, temporary resource, and background operation needs one obvious owner, a defined lifetime, explicit cleanup, and a bound when growth or fan-out is possible. If the owner cannot be named, redesign.

Prefer scoped and structured work. No unowned fire-and-forget work. Propagate cancellation and timeouts; clean up on success, failure, and cancellation.

Use concurrency only for work that can genuinely proceed independently. Bound parallelism, queues, retries, and streams. Avoid many tiny tasks, excessive synchronization hops, and asynchronous wrappers around slow synchronous work.

Prefer immutable sharing or ownership transfer. Isolate shared mutable state behind the smallest suitable mechanism. Do not hold synchronization across blocking, suspending, or unknown external work. Revalidate state after suspension when it may have changed.

Document thread safety, reentrancy, ordering, idempotency, and lifecycle guarantees where callers depend on them.

## Fail Clearly and Safely

- Validate untrusted input at the boundary and normalize it once.
- Do not swallow errors, invent required data, silently weaken security, or use broad defaults to hide invalid state.
- Preserve useful cause, context, cancellation, and timeout semantics. Translate low-level failures only where stable policy is known.
- Make failures actionable: callers should know whether to retry, repair input, wait, reauthenticate, reconfigure, compensate, or report a defect.
- Represent expected absence as absence; represent invalid required data as failure.
- Make retries, backoff, fallback, deduplication, and idempotency explicit, bounded, and semantically safe.
- Use least privilege and safe defaults. Never expose secrets or sensitive payloads through logs, errors, diagnostics, examples, or fixtures.

## Be Naturally Efficient

- Choose appropriate algorithms and representations. Know expected input sizes and the asymptotic cost of non-trivial paths.
- Avoid repeated work, unnecessary allocations and copies, redundant conversion or serialization, N+1 I/O, chatty boundaries, and needless materialization of large data.
- Bound memory, queues, caches, registries, retries, batches, and concurrent work.
- Every cache or retained store needs an owner, limit, invalidation or eviction policy, cleanup behavior, and evidence that it helps.
- Prefer removing work over doing it faster; prefer better data shape over clever control flow.
- Measure representative workloads in a realistic build and environment before adding optimization complexity. Compare before and after; protect known hot paths with benchmarks or regression thresholds.

A performance change must account for latency, throughput, memory, code size, correctness, and maintenance—not one flattering number.

## Prove Behavior, Not Coverage

- Add the smallest deterministic test set likely to catch a meaningful regression.
- Cover the normal path, representative failure, important boundaries, and the reported regression. Add cancellation, concurrency, retry, cleanup, memory, or performance tests only where those behaviors exist or matter.
- Control time, randomness, identifiers, external I/O, environment, locale, ordering, and scheduling when they affect behavior. Do not use arbitrary sleeps as synchronization.
- Test through stable boundaries. Prefer real values and focused fakes at real seams. Do not add production interfaces or broad mocking infrastructure solely to test trivial pass-through code.
- Use realistic fixtures for external formats and observed failures. Coverage is supporting evidence, not the goal.

A useful test fails for a defect users or maintainers would care about. Validation strength must match risk.

## Control Dependencies and Evolution

- Start with standard library, platform, and repository capabilities. Add a dependency only when it is a clear net improvement in correctness, security, maintenance, compatibility, performance, and long-term ownership. Consider transitive cost, update risk, licensing, supply-chain exposure, and operational burden. Avoid thin convenience dependencies.
- Keep third-party types behind project-owned boundaries. Remove dependencies that no longer pay for themselves.
- Generate code only from a canonical, reviewable source through a reproducible process. Do not hand-edit generated output or let generated shapes dictate the human-facing design by accident.
- Add scripts, checks, and CI gates only when they prevent a real repeated mistake or protect a meaningful contract. Automation should remove ceremony, not manufacture it.
- Honor the declared support floor. Prefer additive evolution of public and persisted contracts. Breaking changes require explicit intent, impact analysis, migration guidance, and realistic adoption time.
- Code reveals what it does; comments explain intent, invariants, policy, non-obvious cost, and trade-offs. Keep documentation, tests, schemas, examples, and behavior consistent. Observability should answer operational questions without noise or sensitive data and must not become control flow.

## Complexity Gate

Before adding a type, layer, interface, dependency, task, cache, option, script, extension point, or optimization, answer:

1. What current requirement, measured problem, invariant, or high-impact risk pays for it?
2. Who owns it, calls it, and cleans it up?
3. Why is the simpler direct design insufficient?
4. What cognitive, runtime, compatibility, testing, and maintenance cost does it add?
5. How will its behavior be validated?
6. Can it be removed or replaced later without broad damage?

If the answers are weak or hypothetical, do not add it.

## Reject by Default

- speculative abstractions, forwarding wrappers, one-implementation interfaces without a real boundary, and generic manager/helper/utility/service buckets
- hidden globals, ambient dependency lookup, or implicit I/O, retries, caching, persistence, task creation, and background work
- unbounded resources or concurrency, distribution, plugins, and microservices added for appearance
- unrelated broad refactors and custom frameworks where direct platform primitives are sufficient
- caches without evidence and invalidation, or diagnostics suppressed instead of fixed
- fake tests, vanity coverage, unrealistic benchmarks, and unmeasured performance claims

## Definition of Done

A change is done when it:

- satisfies the stated behavior and preserves critical invariants
- is the smallest coherent solution, without unrelated scope or dead paths
- makes ownership, lifetime, failure, side effects, and resource bounds clear
- keeps public and persisted contracts intentional and documented
- passes meaningful deterministic tests and relevant checks
- contains no obvious avoidable work or unbounded growth, with measurement where risk justifies it
- addresses compatibility, security, migration, and operational impact where relevant
- distinguishes evidence, assumptions, and known limits in the final report

Default response to uncertainty: inspect. To complexity: remove. To hypothetical flexibility: wait. To unsupported scope: fail clearly. To performance claims: measure. To critical risk: be rigorous.
