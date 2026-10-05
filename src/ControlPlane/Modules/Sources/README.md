# Sources

Sources are where a nook's files come from and where its changes go: GitHub repositories a
workspace connected, copied into a chat's nook at `/work/<name>` and pushed back out as branches and
pull requests. The control plane moves the code with each person's own GitHub connection, so no nook
ever holds a token.

## Owns

- **Data:** people's GitHub connections (their user access tokens, sealed, and renewed before they
  expire), connections waiting for the person to approve, the workspace's repositories, and each
  person's git settings (author, committer, AiSloth as co-author, branch prefix).
- **Rules:** who may add and remove a workspace's repositories (Manage) and use them (Read to copy in,
  Write to push); that a push only creates a branch or moves it forward, never forces, and never
  touches the default branch; what commits name by default.
- **Integrations:** GitHub, through the host's GitHub App and `Bagatka.Sdk.GitHub`, and the `git`
  command line on the control plane's computer.

## Does not own

- A nook's copies of its sources and the processes that move files in and out: Nooks, which asks this
  module for a repository's bundle and for who commits name.
- Pushing a chat's changes as one action: the WebApi composes Nooks' export of each source with a push
  here (`Composition/ChatPush.cs`), because this module never asks Nooks.
- Secrets people give their nooks, such as a `GH_TOKEN` for an agent's own `gh`: Secrets.

## Contract

`ISourcesApi` in `Bagatka.AiSloth.Sources.Contracts`. People connect GitHub with the device flow
(start, then complete until they approved), list the repositories their connection reaches, add
them to a workspace, and set their git settings. Nooks exports a repository's branch as a git bundle
with the nook creator's connection; the WebApi pushes a bundle of a nook's changes with the pusher's.

```csharp
Result<ExportedRepository> exported = await sources.ExportAsync(Actor.ForUser(creator), new ExportRepository(repositoryId, Branch: null), bundle, ct);
// ...the nook clones the bundle, works, and its changes come back as another bundle:
Result<PushedChanges> pushed = await sources.PushAsync(pusher, new PushChanges(repositoryId, "main", baseCommit, "aisloth/7f3a2b", PullRequest: true, description), changes, ct);
if (pushed.Failed && pushed.Error == SourcesErrors.NotFastForward) { /* someone else's commits are on that branch: name another */ }
```

## Asks

Workspaces (`GetAccessAsync`), for every call on a workspace's repositories.

## Publishes

Nothing yet.

## Reacts to

Nothing yet.

## Data

Schema `sources`. Tables `github_connections` (one per person: GitHub's ID and login for them, sealed
access and refresh tokens and when they expire; a concurrency token), `github_connection_attempts`
(the sealed device code, when it expires, and when GitHub may be asked next), `repositories` (the
workspace, owner, name, default branch; unique per workspace and name, since the name is a folder),
and `git_settings` (one per person who chose).

## Background work

None. Tokens are renewed when used, one renewal at a time per person (a row lock), as GitHub replaces
the refresh token with each renewal.

## Configuration

`SourcesSettings`: the connection string, the key that seals tokens, and the host's GitHub App
(client ID, client secret, slug), without which GitHub can't be connected. The host also registers
`GitHubClient` with GitHub's addresses, github.com's unless it says otherwise.

## Decisions and constraints

- **One GitHub App per host, acting as each person.** People connect with GitHub's device flow, so a
  CLI needs no callback and the host no public address. A company installs the app on its
  organization's repositories; each member's pushes and pull requests are theirs, under GitHub's own
  permissions and branch protection. `sloth github create-app` makes a host's app with GitHub's
  manifest flow; its "Enable Device Flow" setting is turned on by hand.
- **Tokens never enter a nook.** Repositories go in as git bundles and changes come out as bundles;
  git on the control plane talks to GitHub, with the token only in its environment, as the HTTP
  header it sends (`Git/GitCommand.cs`). Agents can't push or call GitHub with it; people who want
  that give their workspace a `GH_TOKEN` secret.
- **Pushes can't lose anyone's work.** A push fetches the base, adds the bundle's commits, and pushes
  without force: a new branch, or an existing one only when the changes include everything on it.
  The default branch is refused outright.
- **Commits name the person.** By default the author and committer are the person's GitHub account,
  with GitHub's no-reply address so commits link to it, and every commit credits AiSloth as
  co-author through the host app's bot. Each person can name anyone else, drop the co-author, and
  change the branch prefix (`aisloth/`); a push can name any branch.
- **Git is a file-history format, not a product concept.** People see repositories, pushes, and
  pull requests; branches only as names.

## Not built yet

- Other git hosts, such as GitLab, with a token per repository; and repositories by URL.
- Folders AiSloth keeps, with versions, which need object storage (with Checkpoints).
- Bringing new commits from GitHub into a running nook; a nook keeps the branch it started from.
- Revoking a person's authorization at GitHub when they disconnect, and a workspace-wide identity
  policy.
- Two repositories of one name from different owners in one workspace: the second is refused.
- GitHub Enterprise Server's no-reply addresses, and `sloth github create-app` against it.
