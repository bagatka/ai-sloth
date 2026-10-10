import { resolve } from "node:path"
import babel from "@rolldown/plugin-babel"
import tailwindcss from "@tailwindcss/vite"
import react, { reactCompilerPreset } from "@vitejs/plugin-react"
import { defineConfig, type Plugin } from "vite"

// The app lives at /app/ in development, as on a host. Built, its files name each other relatively,
// so a host can serve it at any address (the page's <base>, which the host fills in, anchors them).
// In development the API is on the same origin: everything outside /app/ goes to the WebApi, which
// sees the request as coming from its own pages, as the built app's would.
const api = process.env.AISLOTH_API ?? "http://localhost:5170"

// https://vite.dev/config/
export default defineConfig(({ command }) => ({
  base: command === "build" ? "./" : "/app/",
  server: {
    proxy: {
      "^/(?!app(/|$))": {
        target: api,
        headers: { origin: api },
      },
    },
  },
  plugins: [
    react(),
    // React Compiler memoizes components and hooks, so nobody writes useMemo or useCallback.
    babel({ presets: [reactCompilerPreset()] }),
    tailwindcss(),
    preloadFonts([
      "hanken-grotesk-latin-wght-normal",
      "jetbrains-mono-latin-wght-normal",
    ]),
  ],
  resolve: {
    alias: {
      "@": resolve(import.meta.dirname, "./src"),
    },
  },
}))

// The Latin files of the two fonts every screen draws with are fetched along with the page, so
// text never shows first in a fallback font and then jumps. Other scripts load when text needs them.
function preloadFonts(names: string[]): Plugin {
  return {
    name: "preload-fonts",
    apply: "build",
    transformIndexHtml: {
      order: "post",
      handler(_html, context) {
        return Object.keys(context.bundle ?? {})
          .filter((file) => names.some((name) => file.includes(`/${name}-`)))
          .map((file) => ({
            tag: "link",
            attrs: {
              rel: "preload",
              href: file,
              as: "font",
              type: "font/woff2",
              crossorigin: "",
            },
            injectTo: "head" as const,
          }))
      },
    },
  }
}
