import { useEffect, useState } from "react"
import { ThemeContext, type Theme } from "@/hooks/use-theme"

type ThemeProviderProps = {
  children: React.ReactNode
  defaultTheme?: Theme
  storageKey?: string
}

export function ThemeProvider({
  children,
  defaultTheme = "system",
  storageKey = "ai-sloth-theme",
}: ThemeProviderProps) {
  const [theme, setThemeState] = useState<Theme>(() => {
    try {
      const storedTheme = localStorage.getItem(storageKey)
      return isTheme(storedTheme) ? storedTheme : defaultTheme
    } catch {
      return defaultTheme
    }
  })

  useEffect(() => {
    const root = document.documentElement
    const systemTheme = window.matchMedia("(prefers-color-scheme: dark)")

    function applyTheme() {
      const resolvedTheme =
        theme === "system" ? (systemTheme.matches ? "dark" : "light") : theme

      root.classList.remove("light", "dark")
      root.classList.add(resolvedTheme)
    }

    applyTheme()

    if (theme !== "system") return

    systemTheme.addEventListener("change", applyTheme)
    return () => systemTheme.removeEventListener("change", applyTheme)
  }, [theme])

  function setTheme(nextTheme: Theme) {
    try {
      localStorage.setItem(storageKey, nextTheme)
    } catch {
      // The selected theme still applies when persistence is unavailable.
    }
    setThemeState(nextTheme)
  }

  return (
    <ThemeContext.Provider value={{ theme, setTheme }}>
      {children}
    </ThemeContext.Provider>
  )
}

function isTheme(value: string | null): value is Theme {
  return value === "dark" || value === "light" || value === "system"
}
