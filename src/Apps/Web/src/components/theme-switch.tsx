import { MonitorIcon, MoonIcon, SunIcon } from "@phosphor-icons/react"

import { useTheme } from "@/components/theme-provider"
import { Button } from "@/components/ui/button"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip"

const next = { light: "dark", dark: "system", system: "light" } as const
const icons = { light: SunIcon, dark: MoonIcon, system: MonitorIcon }
const names = { light: "Light", dark: "Dark", system: "Same as the system" }

export function ThemeSwitch({ className }: { className?: string }) {
  const { theme, setTheme } = useTheme()
  const Icon = icons[theme]

  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <Button
            variant="ghost"
            size="icon-sm"
            className={className}
            aria-label={`Theme: ${names[theme]}`}
            onClick={() => setTheme(next[theme])}
          />
        }
      >
        <Icon />
      </TooltipTrigger>
      <TooltipContent>Theme: {names[theme]}</TooltipContent>
    </Tooltip>
  )
}
