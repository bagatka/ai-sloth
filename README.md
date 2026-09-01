# AI Sloth

On-demand, disposable cloud development environments for AI coding agents.

See [`ARCHITECTURE.md`](ARCHITECTURE.md) for module boundaries, ownership, and lifetimes.

## Development

Enter the Nix development environment:

```sh
./dev
```

From `src/api`, `bun run dev` applies the local account, workspace, and session D1 migrations and starts Wrangler.

In another terminal, configure the web UI for the local API and start Vite:

```sh
cd src/web-ui
bun run setup
bun run dev
```

Pass a deployed Worker origin to `bun run setup` when the local UI should use a
remote API. The UI never receives the OpenRouter API key; users obtain bearer
tokens by authenticating through the API.

### Local sandbox safety

Wrangler's local Cloudflare Sandbox runtime creates a Docker container with
`SYS_ADMIN`, `/dev/fuse`, and relaxed AppArmor confinement. Because repository
and agent code is untrusted, run local sessions only on a dedicated or
disposable development/CI host, never on a shared host containing sensitive
data. These privileges belong to the Sandbox runtime and should not be removed
piecemeal without a supported Cloudflare replacement.

On some Docker networks Workerd cannot bind its preferred egress gateway and
reports that it fell back to loopback. Do not suppress the warning. A complete
session that successfully reaches OpenRouter through the configured interceptor
is the smoke test that fallback egress still works. Keep the pinned
`@cloudflare/sandbox` package and Sandbox base-image versions aligned, and test
checkout, model egress, checkpoint creation, backup, and destruction together
when upgrading them.

### Session observability

Every session execution emits one `session.run` summary after cleanup. It
contains only opaque identifiers, deployment and sandbox image versions,
outcome, total duration, failed phase, and phase durations; prompts, model
output, repository URLs, and credentials are excluded. Aggregate these events
to establish p50/p95 latency before changing the bounded execution model.
The service version is the Cloudflare Worker version ID when version metadata
is available and falls back to `local` otherwise.

Authentication rejection logs distinguish a missing bearer header, a malformed
header, and an invalid or expired session without recording the token.

## Accounts and workspaces

Registering creates a user and a seven-day opaque session token:

```sh
curl -X POST http://localhost:8787/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"user@example.com","password":"correct horse battery staple"}'
```

Send the returned token on all authenticated requests:

```http
Authorization: Bearer asl_session_...
```

Create a workspace with `POST /workspaces`. Any member can create a
seven-day invitation with `POST /workspaces/:workspaceId/invitations` and
share the returned token. Another authenticated user accepts it with
`POST /workspace-invitations/accept`. All members have equal permissions to
invite or remove members; a workspace must retain at least one member.

The initial account implementation does not verify email ownership and does not
support password recovery. It should not be opened for unrestricted public
registration until email verification exists.

## Projects

Each workspace can organize a repository's sessions into a nested project tree.
Projects can be created at the repository root or inside another project, moved
up to twelve levels deep, and edited by any workspace member. Each workspace is
limited to 500 projects. Sessions can be
moved only by their controller. The web UI persists these operations through the
workspace project API while retaining drag-and-drop, automatic expansion, and
undo interaction.

Project instructions are inherited from the root project to the session's
immediate project. Starting or continuing a session snapshots the effective
instructions for that revision, so later edits or moves affect only future
turns. Individual instructions are limited to 16 KiB and the combined chain to
32 KiB.

Repository children are listed with
`GET /workspaces/:workspaceId/repositories/:repositoryId/items`. Projects are
created and edited under that repository path; session placement is changed with
`PATCH /workspaces/:workspaceId/repositories/:repositoryId/sessions/:sessionId`.

## Deploy the sandbox

Create a [Cloudflare API token][api-tokens] with these account permissions:

- Account Settings: Read
- Workers Scripts: Edit
- Containers: Edit
- D1: Edit
- Workers R2 Storage: Edit

Then configure credentials and deploy:

```sh
cd src/api
bun install
bun run setup
bun run deploy
```

`setup` validates the account ID and token, then writes them to the ignored `.env` file. `deploy` runs the checks, creates the D1 databases and R2 buckets when missing, applies D1 migrations, and deploys the Worker and sandbox. Repeated deployments use the existing storage. Deployment requires a running Docker-compatible daemon.

Configure the OpenRouter API key after the first deployment:

```sh
bun run secret:openrouter
```

Sandbox backups work through the bound cache bucket without additional secrets.
For production copy-on-write restores and direct multipart R2 transfer, create
an R2 S3 API token scoped to `ai-sloth-sandbox-cache` and configure:

```sh
bun run secret:r2-account-id
bun run secret:r2-access-key-id
bun run secret:r2-secret-access-key
```

Without all three values the sandbox uses the binding-backed backup path, which
is also used by local development. Deployment installs an eight-day lifecycle
rule for physical cleanup of expired backup objects.

## Configure GitHub

Register a GitHub App with expiring user access tokens, the deployed
`/github/callback` URL, read/write repository contents permission, and
read/write pull-request permission. Set its
homepage to the web UI and do not grant repository administration or ruleset
bypass. Then configure the Worker:

```sh
bun run secret:github-client-id
bun run secret:github-client
bun run secret:github-encryption # base64url-encoded 32 random bytes
bun run secret:github-slug
bun run secret:web-ui-origin
```

For local development, copy `src/api/.dev.vars.example` to the ignored
`src/api/.dev.vars`, fill the GitHub values and `OPENROUTER_API_KEY`, and keep
`WEB_UI_ORIGIN=http://localhost:5173`. A signed-in user starts the OAuth
flow with `POST /github/connection`, installs the app on the GitHub accounts they
use, and lists repositories through `GET /github/repositories`.

GitHub connections belong to users, not AI Sloth workspaces. A GitHub
organization may require an administrator to approve the app installation.

## Observe the sandbox

Deployments persist all Worker logs and automatic traces in **Workers & Pages → ai-sloth-sandbox → Observability**. For live debugging, run:

```sh
bunx wrangler tail ai-sloth-sandbox --format pretty
```

Tracing automatically covers Worker handlers, outbound requests, Durable Object calls, D1, and R2. Prompts and session contents are not logged by the application.

## Start a session

Set `ACCOUNT_TOKEN` to an account session token and `WORKSPACE_ID` to one of
the account's workspaces, then run:

```nu
(http post
  --content-type "application/json"
  --headers {
    Authorization: $"Bearer ($env.ACCOUNT_TOKEN)"
    Idempotency-Key: (random uuid)
  }
  $"https://ai-sloth-sandbox.<subdomain>.workers.dev/workspaces/($env.WORKSPACE_ID)/sessions"
  {
    githubRepositoryId: "1296269"
    branch: "main"
    name: "Repository review"
    projectId: null
    prompt: "Review this repository and report the most important issue."
  }
)
```

A successful response returns as soon as the coordinator has durably accepted
the turn. The browser can navigate to the session and follow events while the
agent continues independently of that request:

```json
{
  "sessionId": "b47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
  "turnId": "e47f6e35-b7f3-4c6f-91f6-93f0479ec15b",
  "status": "running"
}
```

## Continue a session

Only the prompt is accepted when continuing. Only the session creator may
continue it. The Worker restores matching Git and Pi artifacts from durable R2;
GitHub is not required:

```nu
(http post
  --content-type "application/json"
  --headers {
    Authorization: $"Bearer ($env.ACCOUNT_TOKEN)"
    Idempotency-Key: (random uuid)
  }
  $"https://ai-sloth-sandbox.<subdomain>.workers.dev/workspaces/($env.WORKSPACE_ID)/sessions/b47f6e35-b7f3-4c6f-91f6-93f0479ec15b/messages"
  {
    prompt: "Where is that behavior tested?"
  }
)
```

Each prompt creates a durable turn. Normalized, sequenced events are journaled
by the session coordinator before clients can read them. Completion stores one
AI Sloth NDJSON transcript, a self-contained Git checkpoint, and a matching Pi
JSONL snapshot in durable R2. D1 advances the revision only after all three
exist. A failed turn retains its transcript but creates no revision. Concurrent
messages based on the same revision cannot both commit; one receives
`409 Conflict`.

Authenticated clients replay and follow a turn without parsing Pi state:

```sh
curl -N \
  -H "Authorization: Bearer $ACCOUNT_TOKEN" \
  -H "Accept: application/x-ndjson" \
  "https://ai-sloth-sandbox.<subdomain>.workers.dev/workspaces/$WORKSPACE_ID/sessions/<session-id>/turns/<turn-id>/events?after=0&follow=true"
```

Each request uses one fresh sandbox. Pi has the `read`, `bash`, `edit`, and
`write` tools and runs as an unprivileged `agent` user without source-control
credentials. Successful `edit` and text `write` activity includes a bounded
unified patch in its tool event; the web UI renders complete patches as lazy
inline diffs and falls back to text for truncated or legacy diff payloads.
While a turn is active, each `bash`, `edit`, or `write` tool invalidates the
current live aggregate. The open **Changes** panel requests a bounded Git
snapshot from the session's immutable base commit to the current Git-visible
working tree after the tool completes, replaces the whole aggregate, and never
composes tool patches. Active snapshots use a temporary index, include tracked changes,
deletions, renames, and non-ignored untracked files, do not alter the repository
index, and remain provisional and memory-only. Their controller-authorized,
turn-scoped endpoint is:

```text
GET /workspaces/:workspaceId/sessions/:sessionId/turns/:turnId/working-diff
```

Each completed revision also stores one authoritative aggregate patch from the
session's immutable base commit to that revision. The panel switches to this
artifact after completion, so updates to the source branch after session
creation do not alter the comparison. The authenticated revision endpoint is:

```text
GET /workspaces/:workspaceId/sessions/:sessionId/diff
```

After Pi exits, every agent process is stopped before the trusted
Git checkpoint. Warm project and hot session backups accelerate restoration but
are disposable and never replace durable artifacts. Repositories with a
`package-lock.json` receive `npm ci --ignore-scripts` preparation before their
warm backup is created.

GitHub receives no session branch until the controller explicitly publishes:

```sh
curl -X POST \
  -H "Authorization: Bearer $ACCOUNT_TOKEN" \
  "https://ai-sloth-sandbox.<subdomain>.workers.dev/workspaces/$WORKSPACE_ID/sessions/<session-id>/publish"
```

Publish restores the exact durable checkpoint in a fresh sandbox, advances only
`ai-sloth/<session-id>` with expected-head protection, and creates or recovers
the session's draft pull request. Repeating publish is idempotent.

The controller can discard durable session state and its hot backup. Published
GitHub branches and pull requests are intentionally left intact:

```sh
curl -X DELETE \
  -H "Authorization: Bearer $ACCOUNT_TOKEN" \
  "https://ai-sloth-sandbox.<subdomain>.workers.dev/workspaces/$WORKSPACE_ID/sessions/<session-id>"
```

Repository checkout and Pi each have a five-minute timeout; checkpoint creation
and push have a two-minute shared deadline. Each account may perform at most five session operations per minute and may own
at most twenty durable sessions. Prompts are limited to 16 KiB, collected Git output to 64 KiB, UI transcripts to 8 MiB per turn, Pi session snapshots to 16 MiB, Git checkpoints to
1,000 changed files, 64 MiB of changed file content, and a 64 MiB durable bundle,
and sessions to 100 turns and 100 successful revisions. Ignored files and dependency/build caches are
not durable; only Git-visible repository state is checkpointed.

[api-tokens]: https://dash.cloudflare.com/?to=/:account/api-tokens&permissionGroupKeys=%5B%7B%22key%22%3A%22account_settings%22%2C%22type%22%3A%22read%22%7D%2C%7B%22key%22%3A%22workers_scripts%22%2C%22type%22%3A%22edit%22%7D%2C%7B%22key%22%3A%22containers%22%2C%22type%22%3A%22edit%22%7D%2C%7B%22key%22%3A%22d1%22%2C%22type%22%3A%22edit%22%7D%2C%7B%22key%22%3A%22workers_r2%22%2C%22type%22%3A%22edit%22%7D%5D&name=AI%20Sloth%20Sandbox%20Deploy
