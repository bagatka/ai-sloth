import path from "path"
import tailwindcss from "@tailwindcss/vite"
import react from "@vitejs/plugin-react"
import { defineConfig, loadEnv } from "vite"

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const environment = loadEnv(mode, import.meta.dirname, "")
  const apiProxy = {
    target: environment.API_PROXY_TARGET ?? "http://localhost:8787",
    changeOrigin: true,
  }

  return {
    plugins: [react(), tailwindcss()],
    resolve: {
      alias: {
        "@": path.resolve(import.meta.dirname, "./src"),
      },
    },
    server: {
      proxy: {
        "/auth": apiProxy,
        "/github": apiProxy,
        "/workspace-invitations": apiProxy,
        "/workspaces": apiProxy,
      },
    },
  }
})
