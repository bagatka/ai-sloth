# Glossary

One name per concept, used the same way in code, APIs, storage, UI, and conversation.

- Don't introduce synonyms.
- Don't reuse a name for something else.
- Add terms in the same change that introduces them.

## Architecture terms

These are the same in every product built from this template.

| Term | Meaning | Lives in | Don't call it |
|---|---|---|---|
| Module | A capability with one contract, its own data, and an internal implementation | `src/Modules/<Module>` | service (until extracted), component, domain |
| Contract | A module's public interface `I<Module>Api` and its records | `<Module>.Contracts` | facade, client, port |
| Feature | One contract method, implemented in one file | `Features/` | use case, handler, command handler |
| Command | Record carrying input to a state-changing feature | Contracts | request, DTO |
| DTO | Record a contract returns | Contracts | model, view model, response |
| Request | HTTP input record in the gateway | `Endpoints/` | command |
| Gateway | The HTTP edge and composition root | `Company.Product.WebApi` | API layer, BFF, controllers |
| Composition | A gateway response assembled from several modules | `Composition/` | aggregator, orchestrator |
| Actor | Who performs a call: a user, a named system process, or anonymous | `Company.Platform` | current user, principal, caller |
| Entity | Persisted object that owns its state changes and rules | `Model/` | model, aggregate |
| Value type | Small immutable type that owns a validation rule | `Model/` | value object, wrapper |
| Typed ID | Strongly typed identifier of an entity | Contracts | key, raw Guid |
| Result / Error | Returned value for expected outcomes; an error has a code and a kind | `Company.Platform` | exception |
| Integration event | A fact a module publishes after commit | Contracts | domain event, message, notification |
| Reaction | A module's handler for another module's event | `Reactions/` | consumer, subscriber, listener |
| Outbox | Where a module's events go: entity methods receive `IOutbox` and add events, which commit in the same transaction as the change | module schema | queue, event bus |
| Job | Background work owned by a module | `Jobs/` | worker, cron, task |
| Platform | General-purpose plumbing shared by all modules | `src/Platform` | shared kernel, common, core, utils |
| Sdk client | General-purpose client for a third-party API | `src/Sdk` | integration, adapter, wrapper |

## Product terms

Fill in per product.

| Term | Meaning | Owner module | Don't call it |
|---|---|---|---|
| _Member (example)_ | _A user's membership in an organization, with a role_ | _Organizations_ | _participant, seat_ |
