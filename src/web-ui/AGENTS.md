# Web UI Engineering Guide

This guide specializes the repository standard for the Vite and React client.

## Boundaries

- The web UI owns browser presentation and interaction. Product behavior and persistence remain behind the HTTP API.
- Organize code around product capabilities. A module should expose the smallest clear component or function contract while hiding transport, storage, framework, and vendor details.
- Prefer concrete modules. A module contract is its small observable behavior, not necessarily a TypeScript `interface` or indirection layer. A replacement should be implementable from that contract without knowing the old internals.
- Add a TypeScript interface or alternate implementation only when a real boundary or variation requires it.
- Do not add a router, global state library, query framework, form framework, or client abstraction until current behavior demonstrates the need.

## React

- Render from props and authoritative data. Derive values during render instead of mirroring them in state.
- Keep state at the lowest owner and model only states the user can actually enter.
- Use effects only to synchronize with an external system such as the DOM, browser media queries, storage events, or a network subscription. Keep each effect scoped, bounded, and cleaned up.
- Handle user actions in event handlers. Do not use effects to react to events or copy data between components.
- Prefer native, uncontrolled form behavior and `FormData` unless interactive behavior requires controlled state.
- Do not render an enabled control without implemented behavior. Omit or explicitly disable unavailable product capabilities.
- Keep async work owned by the initiating route or component, propagate cancellation when work can outlive it, and never retain sensitive response data accidentally.

## Authentication

- Authentication uses the API's opaque bearer session contract, not cookies. Clients send the token in the `Authorization` header.
- The client authentication module must own plaintext token storage, expiry, attachment, and deletion. Never log, include in URLs, or expose tokens in errors or diagnostics.
- Platform storage is an implementation detail: mobile uses platform-protected storage; the browser implementation must document its chosen storage and threat model before persistence is added.
- Do not expose OAuth controls as functional until their API endpoints and failure semantics exist.

## Components and styling

- Add only shadcn components required by current UI:

  ```sh
  bunx --bun shadcn@latest add <component>
  ```

- shadcn output is project-owned source. Review generated behavior, remove unsupported demo paths, and avoid preserving unused variants or exports merely because the registry supplied them.
- Prefer semantic HTML and accessible platform behavior over custom stateful replicas.
- Do not ship source artwork directly. Generate right-sized WebP or AVIF variants, import them through Vite for hashed filenames, provide intrinsic dimensions, and defer non-critical images.

## Validation

From `src/web-ui`, run:

```sh
bun run format
bun run lint
bun run typecheck
bun run build
```
