# <Module>

One sentence: the capability this module provides, in terms a caller understands.

## Owns

- **Data:** what this module is the source of truth for.
- **Rules:** the decisions only this module makes.
- **Integrations:** vendors used internally, if any.

## Does not own

Things a reader might expect here that live elsewhere, and where they live.

## Contract

`I<Module>Api` in `Bagatka.AiSloth.<Module>.Contracts`. In two or three sentences, describe
what callers use it for. Don't list the methods; the interface does that.

## Asks

Modules whose contracts this module calls, and why. Remember that the asks graph must stay
acyclic.

## Publishes

Integration events this module publishes, and when.

## Reacts to

Other modules' events this module handles, and what it does in response.

## Data

Schema `<module>`. List the main tables and anything non-obvious: concurrency tokens, query
filters, retention.

## Background work

Jobs, their schedule, and what they claim and process.

## Configuration

Section `Modules:<Module>`. List the settings that differ between environments.

## Decisions and constraints

Non-obvious choices, and constraints a future change must respect. Delete this section if it's
empty.

## Not built yet

Designed behavior that doesn't exist yet, and cases deliberately left unhandled, each with what
happens today. Remove a line in the change that builds it. Rare cases in code are marked
`// Not handled:` instead (PATTERNS.md, entry 11).
