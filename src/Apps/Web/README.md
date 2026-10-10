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
  and leaves gently. A press shows at once in the material (pressed in, or darker) and never moves
  or resizes anything, which would redraw icons and text off the pixel grid. `pop` is for
  anything that opens.
- **Signature:** the algae panel, shown while an agent works.

`src/components/ui` holds the components shadcn added, with Phosphor icons, restyled to use it. They
are ours: edit them in place. A component added later with `npx shadcn@latest add <name>` arrives in
shadcn's own style and needs the same treatment.

## Rules the build enforces

- **No effects in app code.** `useEffect` and `useLayoutEffect` fail lint outside `components/ui`.
  Data comes through the router and the query cache, subscriptions through `useSyncExternalStore`,
  derived values are computed while rendering, and events are handled in their handlers. Keyboard
  shortcuts go through `useShortcut`, the one hook allowed to subscribe to the window.
- **Memoization is React Compiler's job.** Don't write `useMemo` or `useCallback`.
- **Only `transform` and `opacity` move.** Anything that animates otherwise repaints every frame.
