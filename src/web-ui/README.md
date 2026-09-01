# AI Sloth Web UI

The React web client for AI Sloth, built with Vite and shadcn/ui.

## Development

Start the API on its default local address, then configure and start the UI:

```sh
cd src/web-ui
bun run setup
bun run dev
```

`setup` defaults to `http://localhost:8787` and writes the selected API origin to
the ignored `.env.local` file. To develop the local UI against a deployed API,
pass its origin directly:

```sh
bun run setup https://ai-sloth-sandbox.<subdomain>.workers.dev
bun run dev
```

Vite proxies API requests through the local UI server, so browser code uses
same-origin paths such as `/auth/login` and `/workspaces` in both modes. No
API secret belongs in the web bundle: users authenticate with email and password,
and the authentication response supplies their opaque bearer token.

The authentication module owns token expiry and revocation and never exposes the
token to components. It stores the session in `sessionStorage`, so a reload in
the same tab restores the session while closing the tab clears it. Unlike shared
persistent storage, this does not retain credentials across browser sessions;
any script that compromises the active origin can still access browser storage.

After authentication, the workspace module loads memberships through the
authenticated request capability without receiving the bearer token. Accounts
without a workspace can create one or accept a single-use invitation code.

The main workspace consumes the small cursor-paginated `RepositoryCatalog`
contract. It loads repositories from the signed-in user's GitHub connection and
loads durable nested projects and sessions from the selected workspace. Every
collection loads automatically near either scroll boundary and retains a bounded
sliding window: up to 100 repositories and 50 items per expanded collection. For each loaded page, the first child page of
every visible project is prefetched. Opening a project therefore uses the
prefetched page immediately and starts prefetching one level deeper. Sessions
and projects can be created, dragged with a live destination preview, nested, or
moved back to an ancestor. Hovering a collapsed destination for one second
expands it, and completed moves offer a six-second undo action. Expanded
repositories and projects survive reloads in a bounded 500-entry `localStorage`
preference. Project settings edit inherited agent instructions. The in-memory
catalog remains only as a deterministic interaction test implementation.

Add shadcn/ui components only as they are needed:

```sh
bunx --bun shadcn@latest add <component>
```

## Static assets

Import production assets from `src` so Vite emits content-hashed filenames. The
hosting platform must serve `/assets/*` with long-lived immutable caching and
serve `index.html` without immutable caching so deployments can reference new
asset hashes safely.
