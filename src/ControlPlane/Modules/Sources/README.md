# Sources

Planned. Sources are where a nook's files come from: a workspace's connected repositories and the
folders AiSloth keeps for it. A nook mounts each of its sources at `/work/<name>`, so one chat can
work across several at once.

## Owns

- **Data:** the workspace's sources, each a **repository** (any git remote: GitHub, GitLab, or
  another) or a **folder** (files AiSloth keeps, starting empty or from an upload, with versions),
  and each source's recipe.
- **Rules:** who may use a source, how changes leave a nook for each kind, and the push policy
  (only the agent's own branches, never force-push, never the default branch).
- **Integrations:** git hosts, through credentials only this module holds; object storage for
  folder versions.

## Does not own

- Nooks and the files inside them: Nooks. A nook mounts sources; it never hands them back by
  itself.
- Projects: a project lists sources, but sources work without one.

## Contract

`ISourcesApi` (planned). Members connect repositories, create and upload folders, and edit
recipes; Nooks reads what to mount and how to set it up; delivery operations take changes out.

## Asks

Workspaces.

## Publishes

Nothing yet.

## Reacts to

Nothing yet.

## Data

Schema `sources`: sources, folder versions, recipes and their versions. Folder contents live in
object storage.

## Background work

None yet.

## Configuration

Git host app credentials and the object storage container, passed as settings by the host.

## Decisions and constraints

- **Two kinds, symmetric.** Every source can be downloaded with a nook's changes. A folder can also
  save them as its next version, so later nooks start from it. A repository can push a branch or
  open a pull request instead.
- **Git is our file-history format, not a product concept.** Every source is a git repository
  inside the nook (folders get `git init`), so changes, checkpoints, and forks work the same for
  every kind. Users never need git.
- **No remote tokens in nooks.** The control plane moves code itself: it fetches with its own
  credentials and copies the commits into the nook, and to push it copies the nook's commits out
  and pushes them. Nooks never talk to a git remote with our credentials.
- **Recipes belong to sources.** A recipe is a setup script that is safe to run again, owned by
  AiSloth rather than committed to the repository, and versioned on every edit. An agent can write
  one, test it in a fresh nook, and repair it when it breaks. A nook with several sources runs each
  source's recipe.
- **Sources never ask Nooks.** Delivery that needs a nook's files is composed above: the caller
  exports the changes from Nooks, then delivers them here.
