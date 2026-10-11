import * as React from "react"
import {
  MessageScroller as MessageScrollerPrimitive,
  useMessageScroller,
  useMessageScrollerScrollable,
  useMessageScrollerVisibility,
} from "@shadcn/react/message-scroller"
import { cn } from "cn"

import { Button } from "@/components/ui/button"
import { ArrowDownIcon } from "@phosphor-icons/react"

function MessageScrollerProvider(
  props: React.ComponentProps<typeof MessageScrollerPrimitive.Provider>
) {
  return <MessageScrollerPrimitive.Provider {...props} />
}

function MessageScroller({
  className,
  ...props
}: React.ComponentProps<typeof MessageScrollerPrimitive.Root>) {
  return (
    <MessageScrollerPrimitive.Root
      data-slot="message-scroller"
      className={cn(
        "group/message-scroller relative flex size-full min-h-0 flex-col overflow-hidden",
        className
      )}
      {...props}
    />
  )
}

// Keys that scroll toward the end; Shift+Space scrolls back.
const towardEnd = new Set(["ArrowDown", "End", "PageDown", " "])

function MessageScrollerViewport({
  className,
  onWheel,
  onKeyDown,
  onTouchEnd,
  ...props
}: React.ComponentProps<typeof MessageScrollerPrimitive.Viewport>) {
  const { scrollToEnd } = useMessageScroller()
  // Any wheel, touch, or scrolling key of the reader's stops the following, and reaching the end
  // resumes it as the viewport scrolls there. At the end itself nothing scrolls, so a wheel or key
  // toward it there (a trackpad's last flicks) resumes it here, as it would have stopped it for good.
  function followAtEnd(viewport: HTMLElement) {
    const left =
      viewport.scrollHeight - viewport.scrollTop - viewport.clientHeight
    if (left <= endThreshold) {
      scrollToEnd({ behavior: "auto" })
    }
  }
  return (
    <MessageScrollerPrimitive.Viewport
      onWheel={(event) => {
        onWheel?.(event)
        if (event.deltaY >= 0) {
          followAtEnd(event.currentTarget)
        }
      }}
      onKeyDown={(event) => {
        onKeyDown?.(event)
        if (towardEnd.has(event.key) && !event.shiftKey) {
          followAtEnd(event.currentTarget)
        }
      }}
      onTouchEnd={(event) => {
        onTouchEnd?.(event)
        followAtEnd(event.currentTarget)
      }}
      data-slot="message-scroller-viewport"
      className={cn(
        "size-full min-h-0 min-w-0 scroll-fade-y scrollbar-thin [scrollbar-gutter:stable_both-edges] overflow-y-auto overscroll-contain rounded-xl contain-content focus-visible:-outline-offset-2 data-autoscrolling:scrollbar-thumb-transparent data-autoscrolling:scrollbar-track-transparent data-pending-scroll:invisible",
        className
      )}
      {...props}
    />
  )
}

// How near its end, in pixels, the viewport counts as at it: the scroller's own default.
const endThreshold = 8

function MessageScrollerContent({
  className,
  ...props
}: React.ComponentProps<typeof MessageScrollerPrimitive.Content>) {
  return (
    <MessageScrollerPrimitive.Content
      data-slot="message-scroller-content"
      className={cn("flex h-max min-h-full flex-col gap-6", className)}
      {...props}
    />
  )
}

function MessageScrollerItem({
  className,
  scrollAnchor = false,
  ...props
}: React.ComponentProps<typeof MessageScrollerPrimitive.Item>) {
  return (
    <MessageScrollerPrimitive.Item
      data-slot="message-scroller-item"
      scrollAnchor={scrollAnchor}
      className={cn(
        "-mx-3 -my-1 min-w-0 shrink-0 px-3 py-1 [contain-intrinsic-size:auto_10rem] [content-visibility:auto]",
        className
      )}
      {...props}
    />
  )
}

function MessageScrollerButton({
  direction = "end",
  className,
  children,
  render,
  variant = "ghost",
  size = "icon-sm",
  ...props
}: React.ComponentProps<typeof MessageScrollerPrimitive.Button> &
  Pick<React.ComponentProps<typeof Button>, "variant" | "size">) {
  return (
    <MessageScrollerPrimitive.Button
      data-slot="message-scroller-button"
      data-direction={direction}
      data-variant={variant}
      data-size={size}
      direction={direction}
      className={cn(
        "absolute inset-s-1/2 -translate-x-1/2 rounded-full bg-card text-foreground shadow-float transition-[translate,scale,opacity] duration-200 hover:bg-muted hover:text-foreground data-[active=false]:pointer-events-none data-[active=false]:scale-95 data-[active=false]:opacity-0 data-[active=false]:duration-150 data-[active=true]:translate-y-0 data-[active=true]:scale-100 data-[active=true]:opacity-100 data-[direction=end]:bottom-4 data-[direction=end]:data-[active=false]:translate-y-full data-[direction=start]:top-4 data-[direction=start]:data-[active=false]:-translate-y-full rtl:translate-x-1/2 data-[direction=start]:[&_svg]:rotate-180",
        className
      )}
      render={render ?? <Button variant={variant} size={size} />}
      {...props}
    >
      {children ?? (
        <>
          <ArrowDownIcon />
          <span className="sr-only">
            {direction === "end" ? "Scroll to end" : "Scroll to start"}
          </span>
        </>
      )}
    </MessageScrollerPrimitive.Button>
  )
}

export {
  MessageScrollerProvider,
  MessageScroller,
  MessageScrollerViewport,
  MessageScrollerContent,
  MessageScrollerItem,
  MessageScrollerButton,
  useMessageScroller,
  useMessageScrollerScrollable,
  useMessageScrollerVisibility,
}
