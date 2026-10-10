import * as React from "react"

// The frame every group of components on the showcase page sits in, named so we can talk about
// each one while restyling.
export function Section({
  id,
  title,
  children,
}: {
  id: string
  title: string
  children: React.ReactNode
}) {
  return (
    <section id={id} className="flex scroll-mt-16 flex-col gap-6">
      <h2 className="text-xl font-semibold tracking-tight">{title}</h2>
      <div className="grid gap-6 lg:grid-cols-2">{children}</div>
    </section>
  )
}

export function Specimen({
  name,
  wide = false,
  children,
}: {
  name: string
  wide?: boolean
  children: React.ReactNode
}) {
  return (
    <div
      className={
        wide ? "flex flex-col gap-3 lg:col-span-2" : "flex flex-col gap-3"
      }
    >
      <p className="text-sm text-muted-foreground">{name}</p>
      <div className="flex min-w-0 flex-wrap items-start gap-3 rounded-xl border p-6">
        {children}
      </div>
    </div>
  )
}
