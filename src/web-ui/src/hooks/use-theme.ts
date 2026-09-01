import { createContext, useContext } from "react"

export type Theme = "dark" | "light" | "system"

export type ThemeContextValue = {
  theme: Theme
  setTheme(theme: Theme): void
}

export const ThemeContext = createContext<ThemeContextValue | null>(null)

export function useTheme() {
  const context = useContext(ThemeContext)
  if (!context) {
    throw new Error("useTheme must be used within ThemeProvider")
  }
  return context
}
