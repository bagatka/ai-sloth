# Product and Software Design

## Purpose

Build useful software that is easy to use, understand, and change.

Simplicity means reducing what a person must know to accomplish a task safely. Optimize for the user, the caller, the maintainer, and the operator—not for fewer lines, more modules, smaller diffs, or architectural appearance.

For substantial systems, aim for a small number of understandable capabilities, connected by explicit contracts. Each capability may contain its own well-bounded parts. Stop decomposing when another boundary would add more coordination than understanding.


Correctness, security, privacy, data integrity, and required compatibility are constraints, not tradeable polish. Reduce scope before weakening them. Meet realistic performance and resource requirements without speculative machinery.

Follow higher-priority instructions and applicable repository guidance. Keep repository commands, supported versions, architecture maps, and stack-specific rules local. These principles guide decisions; they do not prescribe one architecture for every problem.

## 1. Match the design effort to the change

Inspect before editing. Read the relevant instructions, product context, callers, contracts, implementation, and tests. Start with the affected area and follow its dependencies; do not inventory the entire repository by default. Distinguish existing guarantees from accidental behavior, and evidence from assumptions.

**Local change within an established contract:** understand the contract, make the direct correction, and verify it. A small script or straightforward function may be the complete design. Do not manufacture modules or a design document.

**New capability, unclear behavior, or a changed boundary:** briefly state the intended outcome, scope, affected responsibilities, contract, and verification approach before implementation. A few paragraphs or a usage example in the task discussion are enough. Record durable decisions near the code only when future work will need them.

**High-risk or hard-to-reverse change:** examine the relevant failure modes and compatibility implications. Compare meaningfully different approaches, including the simplest direct approach. Use a focused experiment when an important assumption can be tested more cheaply than debated.

Risk depends on consequences and coupling, not diff size. Increase design effort when a small edit changes permissions, persisted meaning, shared contracts, or irreversible behavior.

Ask when missing information materially changes the intended outcome or safety of the work. Otherwise state the simplest reasonable assumption and proceed. Do not turn routine work into an approval ceremony.

## 2. Shape the product before the implementation

Start with the person or system trying to accomplish something. Establish what they need to finish, what makes that difficult today, and what observable result would make this change worthwhile. For internal work, the user may be another developer or an operator.

Describe the normal workflow and an important failure or recovery path before choosing the architecture. For a UI, sketch the interaction; for a CLI, an invocation and its output; for a library, a caller example. Make the intended experience concrete enough to question.

Identify what is required and what is deliberately out of scope. Consider whether an existing capability, better default, simpler workflow, or removal of behavior solves the problem without a new subsystem. Do not silently substitute a different outcome for the one requested; explain material trade-offs.

Prefer a coherent default path over a collection of configurable possibilities. Add a choice when distinct supported needs justify it, not because the design has avoided making a decision. Keep terminology and behavior consistent with the surrounding product and platform.

Treat first use, discoverability, feedback, empty states, failure messages, and recovery as part of the behavior where relevant. User-facing interfaces must account for accessibility. Do not expose internal architecture as steps the user has to coordinate.

Choose the smallest complete slice that delivers the intended value. Small scope should still produce an understandable experience, including its important failures. A prototype may explore less, but must not be presented as production-ready behavior.

## 3. Find boundaries that make reasoning local

Before distributing substantial work across files or agents, identify the important responsibilities and the knowledge each should own. Extend a suitable existing module before inventing another.

A useful module owns a coherent capability, invariant, representation, resource lifecycle, or external integration. Its contract explains what it does; its implementation contains the knowledge callers should not need. A module can be a function, type, package, library, or service. A directory alone does not establish a boundary.

Split responsibilities when they hide different decisions, change for different reasons, or can be understood and verified independently. Keep them together when separation would require shared internals, chatty coordination, or a fragile cross-module invariant. Do not split merely by execution step, file size, or architectural fashion.

For each substantial boundary, be able to explain:

- What it owns, and what it explicitly does not own.
- What callers may rely on without reading its implementation.
- Which dependencies it needs, and which details it hides.

Apply the same reasoning inside a large module. Expose the parent's contract to the rest of the system; keep internal submodules private unless another caller has a real need. Do not impose a fixed number of layers or equal-sized components.

When substantial work changes system structure, update the local architecture map—or add a short one if it is missing. Show major responsibilities, dependency direction, and contract locations; do not catalogue every file or duplicate the contracts.

Prefer deep modules: a modest interface that hides substantial useful work. Count the concepts, sequencing rules, and obligations an interface imposes, not just its methods or parameters. One function taking an unstructured options bag is not necessarily simple.

Give each important rule and mutable state one authoritative owner. Other modules ask that owner or consume its defined results; they do not reproduce its rules or mutate its internals. Similar-looking code need not be unified when it represents different knowledge.

Keep dependency direction explicit. Avoid cycles and reaching through one module into another's private parts. When a cycle appears, reconsider ownership or composition before adding another indirection.

Independence means local reasoning and change within a contract. It does not require separate deployment, repositories, processes, or a plugin system. Prefer in-process composition until a real operational constraint justifies distribution.

## 4. Design contracts from the caller inward

For a new or materially changed boundary, write a representative use before its implementation. Exercise the ordinary case and a consequential failure. Improve the interface if callers must understand storage layouts, reconstruct policy, coordinate hidden state, or perform a fragile sequence of calls.

A contract is the behavior a caller may rely on, not merely a signature or an interface type. State the relevant parts of:

- **Meaning:** the responsibility, inputs, outputs, and observable guarantees.
- **Validity:** preconditions, invariants, and expected absence or rejection.
- **Effects:** state changes, I/O, ownership, and lifetime.
- **Failure:** error meaning, partial completion, and recovery obligations.
- **Limits:** ordering, concurrency, cancellation, idempotency, and resource bounds when callers depend on them.

Specify only what matters at that boundary. Do not turn every function into a specification exercise. Use the language's types and visibility, existing schemas, concise documentation, and executable examples or tests. Keep each contract authoritative and close to its implementation; do not create duplicate contract files for ceremony.


Make the normal use obvious. Prefer explicit values and dependencies over ambient context, global registries, or an entire application object. Use representations that eliminate invalid states and special cases when they make the design easier to understand.

Hide implementation choices without hiding operational meaning. A caller should not need the database schema, but may need to know that an operation persists data, can partially succeed, or performs expensive I/O. An interface must not promise atomicity, durability, or safe retries unless the implementation provides them.

Existing contracts must be checked against actual behavior when they are uncertain. Do not treat stale documentation or a convenient test double as proof.

## 5. Compose useful capabilities, not speculative frameworks

Make a capability useful on its own terms rather than entangled with one screen, command, or workflow. Prefer inputs and results that a different caller could understand without recreating the original application's environment.

Separate product-specific coordination from mechanisms when that separation removes knowledge from the mechanism. Keep an invariant with the module that can enforce it reliably. Do not push every difficult decision into callers in the name of flexibility.

For example, report formatting can accept a report snapshot and a supported format, then return a document or a defined error. It need not also select database records, interpret HTTP requests, and send email. A web handler or scheduled job can compose those responsibilities. A one-off export script may still perform the whole workflow directly when reusable boundaries would provide no benefit.

A meaningful boundary can justify a module with one caller or one implementation. Multiple callers are not a prerequisite for information hiding. But do not add interfaces, adapters, extension hooks, or configuration solely for hypothetical future uses.

A thin adapter is worthwhile when it translates a real boundary or prevents external details from spreading. A forwarding layer that adds no isolation, policy, or useful vocabulary is not. Do not wrap stable platform types or duplicate models solely for architectural purity.


Prefer mature repository, platform, and standard-library capabilities where suitable. Add dependencies for clear net value after considering maintenance, security, compatibility, and operational cost. Isolate external details where changing them would otherwise affect unrelated code.


Keep internal reuse distinct from a public compatibility commitment. Do not publish an API or extract a package merely because a component might eventually be reusable. Leave room to evolve by hiding decisions, not by prebuilding extension points.

## 6. Learn through working slices

For non-trivial design decisions, test the strongest alternative against the proposed design. Would a direct implementation, an existing capability, a different data model, or fewer boundaries do better? Compare caller burden, hidden knowledge, failure behavior, and the spread of likely changes—not aesthetic preference.

Sketch enough of the system to choose the next useful piece. Do not design every internal layer before learning from implementation. When feasibility is uncertain, run a focused spike, identify what it established, and revisit the contract before building on it.


Build in increments with observable results. A component-level test or small harness can be the first demonstration; connect the pieces into a real workflow early. Do not finish a collection of isolated subsystems before testing whether they compose into something useful.

Keep contracts provisional while exploring, explicit while integrating, and intentionally managed once callers depend on them. Revise an awkward boundary rather than accumulating workarounds around it. Keep unfinished or simulated behavior out of the production path.

When delegating, divide work along understood responsibilities. Give each task its contract, permitted scope, and acceptance checks. Do not let parallel implementations independently invent a shared contract. Integrate and verify the whole behavior; locally passing pieces are not sufficient.

Make the smallest coherent change, including the narrow boundary repair needed for correctness and clarity. Do not use this standard as permission for unrelated refactors, speculative infrastructure, or formatting churn. Preserve others' work and remove paths made obsolete by the change.

## 7. Preserve the engineering floor

**Failure and security.** Validate untrusted input at the relevant trust boundary. Use least privilege and safe defaults. Represent expected absence honestly; preserve useful error context. Never conceal failure with invented data, silent fallback, or weakened checks. Recovery, retries, and compensation must be explicit and semantically safe. Do not expose secrets or sensitive data through diagnostics or fixtures.

**State and resources.** Give mutable state and resource-consuming work an obvious owner and lifetime. Make persistence, transactions, partial completion, and cleanup deliberate. Propagate cancellation and deadlines where applicable. Bound work, memory, queues, retries, and concurrency. Do not add unowned background work. Irreversible operations require appropriate authorization and a considered recovery strategy.

**Performance.** Choose suitable algorithms and representations for expected scale. Avoid obvious repeated work and unnecessary I/O. Measure relevant workloads before adding optimization complexity. A cache, queue, or concurrent path must have a demonstrated purpose and defined consistency, failure, and resource semantics. Do not transfer accidental implementation complexity into a harder user workflow.

**Code and automation.** Use precise names, visible control flow, and established local conventions. Explain intent, invariants, and non-obvious trade-offs rather than narrating syntax. Generate mechanical artifacts from an authoritative, reviewable source when that reduces maintenance; do not hand-edit generated output. Add tooling to protect meaningful contracts or eliminate repeated mistakes, not to enforce ceremony.

## 8. Verify the product and its boundaries

Check the actual requested outcome, not just the implementation's internal consistency. Use the changed workflow where the environment permits. Inspect the real UI, command output, or caller experience, including an important failure. Automated checks cannot establish every aspect of usability; report what was not exercised.

Test module guarantees through their boundaries, and test the composed path for mismatched assumptions. Include the regression being fixed and risk-relevant failures, limits, and state transitions. Prefer deterministic tests, realistic values, and focused fakes at external seams. Do not couple tests to private structure without a specific reason.

Run the relevant repository checks. Use existing visibility, dependency rules, and architecture checks to preserve boundaries; add a focused check when a consequential boundary needs protection. Do not replace executable evidence with a claim that the design follows these principles.

A compatible implementation change should remain mostly within its owner. A contract change is different: inspect affected callers and persisted or external representations. Changes to defaults, errors, ordering, side effects, and performance guarantees can break consumers even when signatures stay unchanged. Provide migration, rollout, or recovery measures when needed.

Before finishing, ask:

- Does this solve the intended problem with an understandable normal and failure path?
- Can a caller use each changed boundary without learning its implementation?
- Is important knowledge owned once, with internal changes kept local?

- Did any new layer, option, dependency, or state add more burden than it removed?
- What evidence supports the result, and what remains unverified?

Report the outcome, material design or contract changes, checks actually run and their results, and remaining risks or limits. Keep the report proportional to the work. Do not claim checks passed when they were not run.

The desired result is not an impressive architecture. It is a useful product whose parts can be understood, trusted, and changed without holding the whole system in your head.
