import {
  WarningIcon,
  CheckCircleIcon,
  KeyIcon,
  ChatsIcon,
} from "@phosphor-icons/react"

import {
  Alert,
  AlertAction,
  AlertDescription,
  AlertTitle,
} from "@/components/ui/alert"
import { NookState } from "@/components/nook-state"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Empty,
  EmptyContent,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from "@/components/ui/empty"
import {
  Progress,
  ProgressLabel,
  ProgressValue,
} from "@/components/ui/progress"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import { toast } from "@/components/ui/toast"
import { Section, Specimen } from "./section"

export function Feedback() {
  return (
    <Section id="feedback" title="Feedback">
      <Specimen name="Nook states" wide>
        {(
          [
            "starting",
            "ready",
            "working",
            "asleep",
            "evicted",
            "offline",
            "failed",
          ] as const
        ).map((state) => (
          <NookState key={state} state={state} />
        ))}
      </Specimen>
      <Specimen name="Badge: agent accounts" wide>
        <Badge>Claude plan</Badge>
        <Badge variant="secondary">OpenAI API key</Badge>
        <Badge variant="outline">Workspace</Badge>
        <Badge variant="destructive">Sign in again</Badge>
        <Badge variant="ghost">Personal</Badge>
      </Specimen>
      <Specimen name="Alert" wide>
        <Alert>
          <CheckCircleIcon />
          <AlertTitle>Pushed to ada/fix-flaky-checkout</AlertTitle>
          <AlertDescription>
            Pull request #482 is open on acme/web.
          </AlertDescription>
        </Alert>
        <Alert variant="warning">
          <KeyIcon />
          <AlertTitle>Your Claude plan needs you to sign in again</AlertTitle>
          <AlertDescription>Chats on it wait until you do.</AlertDescription>
          <AlertAction>
            <Button size="xs">Sign in</Button>
          </AlertAction>
        </Alert>
        <Alert variant="destructive">
          <WarningIcon />
          <AlertTitle>The nook couldn’t start</AlertTitle>
          <AlertDescription>
            Azure refused the sandbox: the region is out of capacity. Try again
            in a few minutes.
          </AlertDescription>
        </Alert>
      </Specimen>
      <Specimen name="Toast">
        <Button
          variant="outline"
          onClick={() =>
            toast.add({
              title: "Pushed to ada/fix-flaky-checkout",
              description: "Pull request #482 is open.",
              type: "success",
            })
          }
        >
          Push
        </Button>
        <Button
          variant="outline"
          onClick={() =>
            toast.add({
              title: "Couldn’t push",
              description: "GitHub says the branch moved. Pull first.",
              type: "error",
            })
          }
        >
          Push, failing
        </Button>
        <Button
          variant="outline"
          onClick={() =>
            toast.add({
              title: "The disk is nearly full",
              description: "9.4 of 10 GB used in the nook.",
              type: "warning",
            })
          }
        >
          Disk warning
        </Button>
        <Button
          variant="outline"
          onClick={() =>
            toast.add({
              title: "Waking the nook",
              description: "It slept for 3 hours.",
              type: "loading",
            })
          }
        >
          Waking
        </Button>
      </Specimen>
      <Specimen name="Progress and spinner">
        <Progress value={72} className="w-full">
          <ProgressLabel>Disk</ProgressLabel>
          <ProgressValue />
        </Progress>
        <span className="flex items-center gap-2 text-sm text-muted-foreground">
          <Spinner />
          Starting the nook
        </span>
      </Specimen>
      <Specimen name="Skeleton: chat list loading">
        <div className="flex w-full flex-col gap-3">
          {[0, 1, 2].map((row) => (
            <div key={row} className="flex items-center gap-3">
              <Skeleton className="size-8 rounded-full" />
              <div className="flex flex-1 flex-col gap-2">
                <Skeleton className="h-3 w-3/5" />
                <Skeleton className="h-3 w-2/5" />
              </div>
            </div>
          ))}
        </div>
      </Specimen>
      <Specimen name="Empty">
        <Empty className="w-full border border-dashed">
          <EmptyHeader>
            <EmptyMedia variant="icon">
              <ChatsIcon />
            </EmptyMedia>
            <EmptyTitle>No chats yet</EmptyTitle>
            <EmptyDescription>
              Start one and an agent gets a nook of its own with your
              repositories.
            </EmptyDescription>
          </EmptyHeader>
          <EmptyContent>
            <Button>Start a chat</Button>
          </EmptyContent>
        </Empty>
      </Specimen>
    </Section>
  )
}
