# Web app

The web app, in React and TypeScript with [shadcn](https://ui.shadcn.com) components on Base UI.
Its screens aren't built yet. It has two pages for designing them, with made-up content:

- `/`: the chat screen, composed from the components, to judge the look as a whole;
- `/components`: every component, to style them one by one. It loads only when opened.

Run it from `./dev`, which has Node:

```bash
npm install
npm run dev      # http://localhost:5173
npm run build    # type-checks, then builds into dist/
npm run lint
npm run format
```

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
  group. Anything else focused from the keyboard gets the same algae outline from the base styles,
  and mouse focus shows nothing.
- **Motion:** one curve (`--ease-out`) that every transition uses by default. Hover arrives at once
  and leaves gently. A press (`press`) gives a little and sinks in its material at once; it scales
  on its own GPU layer, so icons and text never jump. Nothing else moves on press (lint). `pop` is for
  anything that opens.
- **Signature:** the algae panel, shown while an agent works.

`src/components/ui` holds the components shadcn added, with Phosphor icons, restyled to use it. They
are ours: edit them in place. A component added later with `npx shadcn@latest add <name>` arrives in
shadcn's own style and needs the same treatment.

## Code views

`src/components/code` shows code: `Diff` (a change inline in a chat or in a panel, unified or
split), `CodeFile`, and `FileTree` with each file's change. They are built on
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
