import * as React from "react"

export type SidebarContextValue = {
  state: "expanded" | "collapsed"
  open: boolean
  setOpen: React.Dispatch<React.SetStateAction<boolean>>
  openMobile: boolean
  setOpenMobile: React.Dispatch<React.SetStateAction<boolean>>
  isMobile: boolean
  width: number
  setWidth(width: number): void
  toggleSidebar(): void
}

export const SidebarContext = React.createContext<SidebarContextValue | null>(
  null
)

export function useSidebar() {
  const context = React.useContext(SidebarContext)
  if (!context) {
    throw new Error("useSidebar must be used within a SidebarProvider")
  }
  return context
}
