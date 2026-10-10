import { resolve } from "node:path"
import babel from "@rolldown/plugin-babel"
import tailwindcss from "@tailwindcss/vite"
import react, { reactCompilerPreset } from "@vitejs/plugin-react"
import { defineConfig, type Plugin } from "vite"

// https://vite.dev/config/
export default defineConfig({
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
})

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
              href: `/${file}`,
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
