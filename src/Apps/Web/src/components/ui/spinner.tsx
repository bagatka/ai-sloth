import { cn } from "cn"

// A thin arc on a faint track, turning quickly: a fast spinner makes the same wait feel shorter.
function Spinner({ className, ...props }: React.ComponentProps<"svg">) {
  return (
    <svg
      data-slot="spinner"
      role="status"
      aria-label="Loading"
      viewBox="0 0 16 16"
      fill="none"
      className={cn(
        "size-4 animate-spin [animation-duration:700ms]",
        className
      )}
      {...props}
    >
      <circle
        cx="8"
        cy="8"
        r="6.25"
        stroke="currentColor"
        strokeOpacity="0.2"
        strokeWidth="1.75"
      />
      <path
        d="M14.25 8A6.25 6.25 0 0 0 8 1.75"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinecap="round"
      />
    </svg>
  )
}

export { Spinner }
