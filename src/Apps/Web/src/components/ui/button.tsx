import { Button as ButtonPrimitive } from "@base-ui/react/button"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "cn"

import { Spinner } from "@/components/ui/spinner"

const buttonVariants = cva(
  "group/button inline-flex shrink-0 press items-center justify-center rounded-lg text-sm font-medium whitespace-nowrap focus-ring outline-none select-none disabled:pointer-events-none aria-invalid:border-destructive dark:aria-invalid:border-destructive/50 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
  {
    variants: {
      variant: {
        default: "font-semibold control-primary",
        outline:
          "control-raised disabled:opacity-50 aria-expanded:bg-(--raised-hover)",
        secondary:
          "bg-secondary text-secondary-foreground hover:bg-[color-mix(in_oklch,var(--secondary),var(--foreground)_6%)] active:bg-[color-mix(in_oklch,var(--secondary),var(--foreground)_11%)] disabled:opacity-50 aria-expanded:bg-[color-mix(in_oklch,var(--secondary),var(--foreground)_6%)]",
        ghost:
          "hover:bg-muted hover:text-foreground active:bg-[color-mix(in_oklch,var(--muted),var(--foreground)_6%)] disabled:opacity-50 aria-expanded:bg-muted aria-expanded:text-foreground",
        destructive:
          "bg-destructive/8 font-semibold text-destructive hover:bg-destructive/14 active:bg-destructive/20 disabled:opacity-50 dark:bg-destructive/14 dark:hover:bg-destructive/22",
        link: "text-primary underline-offset-4 hover:underline active:opacity-75 disabled:opacity-50",
      },
      size: {
        default:
          "h-9 gap-2 px-3.5 has-data-[icon=inline-end]:pr-3 has-data-[icon=inline-start]:pl-3",
        xs: "h-7 gap-1.5 rounded-md px-2.5 text-xs has-data-[icon=inline-end]:pr-2 has-data-[icon=inline-start]:pl-2 [&_svg:not([class*='size-'])]:size-3.5",
        sm: "h-8 gap-1.5 px-3 text-[0.8125rem] has-data-[icon=inline-end]:pr-2.5 has-data-[icon=inline-start]:pl-2.5 [&_svg:not([class*='size-'])]:size-3.5",
        lg: "h-10 gap-2 px-4 text-[0.9375rem] has-data-[icon=inline-end]:pr-3.5 has-data-[icon=inline-start]:pl-3.5 [&_svg:not([class*='size-'])]:size-[18px]",
        icon: "size-9",
        "icon-xs": "size-7 rounded-md [&_svg:not([class*='size-'])]:size-3.5",
        "icon-sm": "size-8",
        "icon-lg": "size-10 [&_svg:not([class*='size-'])]:size-[18px]",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  }
)

function Button({
  className,
  variant = "default",
  size = "default",
  loading = false,
  children,
  ...props
}: ButtonPrimitive.Props &
  VariantProps<typeof buttonVariants> & {
    /** Shows a spinner in the label's place, keeping the button's size and color, and ignores presses. */
    loading?: boolean
  }) {
  return (
    <ButtonPrimitive
      data-slot="button"
      aria-busy={loading || undefined}
      aria-disabled={loading || undefined}
      className={cn(
        buttonVariants({ variant, size, className }),
        loading && "pointer-events-none relative"
      )}
      {...props}
    >
      {loading ? (
        <>
          <span className="invisible inline-flex items-center gap-[inherit]">
            {children}
          </span>
          <Spinner className="absolute inset-0 m-auto" />
        </>
      ) : (
        children
      )}
    </ButtonPrimitive>
  )
}

export { Button, buttonVariants }
