# Web app

The web app, in React and TypeScript with [shadcn](https://ui.shadcn.com) components on Base UI. A
host serves it at its app address, `/app/` on its own address by default. Today people sign in and
start chats, choosing their repositories, agent, and account. They see their workspace's chats as
they change, and follow one as its agent works: a message starts the next turn or, sent while the
agent works, goes into its turn, and Esc stops it. They review what the agent changed in its
repositories, push it as a branch or a pull request, and download a chat's files.

Run it from `./dev`, which has Node, with AiSloth running (`dotnet aspire run`):

```bash
npm ci
npm run dev           # http://localhost:5173/app/, talking to the host at localhost:5170
npm run build         # type-checks, then builds into dist/, which the local host serves at /app/
npm run lint
npm run format
npm run api           # the API's types, after the WebApi's openapi.json changes
```

Sign in with a link code: `sloth host link` on a device already signed in. In development, two more
pages show every component and a chat screen with made-up content, to judge the look:
`/app/design/components` and `/app/design`.

## How it's built

- **Routes** (`src/app/router.tsx`): sign-in, and behind it, once the session checks out, a new chat
  (`/`) and a chat (`/chats/<id>`), with the workspace's chats beside them.
- **A new chat** (`src/app/new-chat.tsx`) starts on the host as soon as someone writes, so its nook
  is ready when they send; the browser remembers each workspace's last choices.
- **The API** (`src/app/api.ts`): one client whose types are generated from the WebApi's
  OpenAPI document, so a changed API fails the type check. Failures arrive as `ApiError`, with the
  host's problem details. Queries live in `src/app/queries.ts`, and TanStack Query owns their cache.
- **The session** is a cookie the host sets at sign-in, which no script reads. The app never handles
  a token; a 401 anywhere sends the person to sign in again.
- **Changes** (`src/app/chat/changes-panel.tsx`) come from the host as one patch per repository, as
  `git diff` prints it, split into files (`changes.ts`) for the code views. Reading them wakes an
  asleep nook, so the app asks only when someone opens them.
- **A chat** streams its events from the host (`src/app/chat/chat-stream.ts`), and the transcript
  is a pure fold of them (`transcript.ts`). Following a chat again picks up after the last event
  the app saw, and the browser reconnects by itself. A workspace's chats stream the same way
  (`src/app/workspace-chats.ts`).

## The look

It lives in `src/index.css`:
- **Color:** the landing page's palette, with every grey on one OKLCH hue (115) in both themes;
  algae is the one accent, red marks errors, and amber warnings.
- **Type:** Hanken Grotesk and JetBrains Mono, in named roles: `text-caption`, `text-label`, the
  14px default, `text-read` for messages, and `text-title`.
- **Shape:** a radius per kind of thing, nested concentrically where one sits inside another.
- **Elevation:** `shadow-field` (sunken), `shadow-raised`, `shadow-panel` (the main panel), and
  `shadow-float`.
- **Materials:** raised controls (`control-primary`, `control-raised`), sunken fields (`shadow-field`),
  and floating surfaces (`shadow-float`), all lit from above.
- **Focus:** one look and one owner. `focus-ring` is for things you press, and `focus-field` is
  for text fields. A borderless field inside a group (`focus-field-within`) leaves focus to the
  group, and a scroll area whose content paints over outlines, like the code views, uses
  `focus-ring-over`. Anything else focused from the keyboard gets the same algae outline from the
  base styles, and mouse focus shows nothing.
- **Motion:** one curve (`--ease-out`) that every transition uses by default. Hover arrives at once
  and leaves gently. A press (`press`) gives a little and sinks in its material at once; it scales
  on its own GPU layer, so icons and text never jump. Nothing else moves on press (lint). `pop` is for
  anything that opens.
- **Signature:** the algae panel, shown while an agent works.

`src/components/ui` holds the components shadcn added, with Phosphor icons, restyled to use it. They
are ours: edit them in place. A component added later with `npx shadcn@latest add <name>` arrives in
shadcn's own style and needs the same treatment.

## Code views

`src/components/code` shows code: `Diff` (one change, inline in a conversation), `DiffList` (many
changes in one scroll, unified or split), `CodeFile`, and `FileTree` with each file's change. A list
beside a tree takes its files through `inTreeOrder`, brings a picked file up with `showFile`, and
reports what the reader sees with `onViewChange`: the tree selects the file being read, whose
header the list tints the same way, and draws a thumb beside the files on screen. They are built on
[@pierre/diffs](https://diffs.com) and [@pierre/trees](https://trees.software), which draw in shadow
DOM and take their colors, fonts, and spacing from the `--diffs-*` and `--trees-*` variables in
`src/index.css`. Highlighting runs in workers. The views load on first use: a screen likely to show
code calls `warmUpCode()`, and anything about to open code calls `preloadCode()`, so the first view
shows at once. Expanding unchanged lines takes a pointer, since the library's expander can't take
focus. @pierre/trees is a beta, pinned to an exact version.

## Rules the build enforces

- **No effects in app code.** `useEffect` and `useLayoutEffect` fail lint outside `components/ui`
  and the few files listed in `eslint.config.js`, each syncing something outside React. Data comes
  through the router and the query cache, subscriptions through `useSyncExternalStore`, derived
  values are computed while rendering, and events are handled in their handlers. Keyboard shortcuts
  go through `useShortcut`, the one hook allowed to subscribe to the window.
- **Memoization is React Compiler's job.** Don't write `useMemo` or `useCallback`.
- **Only `transform` and `opacity` move.** Anything that animates otherwise repaints every frame.
