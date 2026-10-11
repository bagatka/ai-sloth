import type { ReactNode } from "react"
import { useMutation } from "@tanstack/react-query"
import { Link } from "@tanstack/react-router"
import {
  CaretDownIcon,
  CheckCircleIcon,
  CircleNotchIcon,
  GitPullRequestIcon,
  WarningCircleIcon,
  WrenchIcon,
} from "@phosphor-icons/react"

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button, buttonVariants } from "@/components/ui/button"
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible"
import { toast } from "@/components/ui/toast"
import { answer, api, messageOf } from "../api"
import { chatStream } from "./chat-stream"
import { usePush } from "./push-menu"
import type { Row, Transcript } from "./transcript"

// The nook's setup on the message box, where people look next, while it needs them. A failed setup is
// fixed in a chat of its own, so the chat it failed in goes on as it is: one click starts it, and the
// chat links to it. The chat that fixes it shows the fix as it goes: the agent fixes it, the setup
// runs again, and once it works, a pull request takes the fix to new chats. The transcript keeps a
// line for each run.

// What "Try again" asks the agent of a chat that fixes a setup, as the person.
const tryAgainRequest = "The setup still fails. Fix it, and commit the fix."

type Setup = Extract<Row, { kind: "setup" }>

export function SetupBanner({
  chatId,
  transcript,
  agent,
  proposing,
}: {
  chatId: string
  transcript: Transcript
  /** The agent's name, such as "pi". */
  agent: string
  /** Whether the signed-in person's messages are only proposals, which can't direct the agent. */
  proposing: boolean
}) {
  const setups = transcript.rows.filter(
    (row): row is Setup => row.kind === "setup"
  )
  const setup = setups.at(-1)
  if (!setup) {
    return null
  }

  const fix = transcript.setupFix
  const fixing = fix?.fixingIn === chatId
  const repository = repositoryOf(setup.scripts)
  // Whose setup it is, to start a sentence and within one.
  const setupOf = repository ? `${repository}'s setup` : "The setup"
  const theSetup = repository ? `${repository}'s setup` : "the setup"
  // The first run shows in the transcript as it runs.
  if (!setup.ended) {
    if (!setup.again) {
      return null
    }
    return (
      <Working>
        {fixing
          ? `Checking the fix: running ${theSetup} again…`
          : "Running the setup again…"}
      </Working>
    )
  }
  if (setup.exitCode === 0) {
    return fixing && setup.again ? (
      <Fixed chatId={chatId} setupOf={setupOf} proposing={proposing} />
    ) : null
  }
  if (fix && !fixing) {
    return <FixedElsewhere fixingIn={fix.fixingIn} setupOf={setupOf} />
  }
  if (fixing && transcript.setupRunAsked) {
    return (
      <Working>
        {`${agent} is fixing ${theSetup}. When it's done, AiSloth runs the setup again to check the fix.`}
      </Working>
    )
  }
  return (
    <Failed
      chatId={chatId}
      setup={setup}
      setupOf={setupOf}
      still={fixing || setups.length > 1}
      fixing={fixing}
      proposing={proposing}
    />
  )
}

function Failed({
  chatId,
  setup,
  setupOf,
  still,
  fixing,
  proposing,
}: {
  chatId: string
  setup: Setup
  setupOf: string
  still: boolean
  fixing: boolean
  proposing: boolean
}) {
  const stream = chatStream(chatId)
  // Elsewhere, a chat of its own fixes it; the chat that fixes it tries again itself.
  const fixElsewhere = useMutation({
    mutationFn: () =>
      answer(
        api.POST("/chats/{id}/setup/fix", { params: { path: { id: chatId } } })
      ),
    onError: (error) =>
      toast.add({
        type: "error",
        title: "Not started",
        description: messageOf(error),
      }),
  })
  const tryAgain = useMutation({
    mutationFn: async (key: string) => {
      const sent = await answer(
        api.POST("/chats/{id}/messages", {
          params: { path: { id: chatId } },
          body: { text: tryAgainRequest },
        })
      )
      await answer(
        api.POST("/chats/{id}/setup", { params: { path: { id: chatId } } })
      )
      return { sent, key }
    },
    onSuccess: ({ sent, key }) => stream.taken(key, sent.id),
    onError: (error, key) => {
      stream.dropPending(key)
      toast.add({
        type: "error",
        title: "Not asked",
        description: messageOf(error),
      })
    },
  })

  return (
    <Banner>
      <Alert variant="warning">
        <WarningCircleIcon />
        <AlertTitle>
          {setupOf} {still ? "still fails" : "failed"}
        </AlertTitle>
        <AlertDescription>
          {fixing
            ? "The agent can try another way."
            : "What it installs is missing here. A new chat can fix it while this one goes on."}
        </AlertDescription>
        {/* min-w-0 keeps long lines of output from widening the alert's grid column. */}
        <Collapsible className="col-start-2 mt-2 min-w-0">
          <div className="flex items-center gap-2">
            {!proposing &&
              (fixing ? (
                <Button
                  size="sm"
                  loading={tryAgain.isPending}
                  onClick={() =>
                    tryAgain.mutate(stream.addPending(tryAgainRequest))
                  }
                >
                  <WrenchIcon data-icon="inline-start" />
                  Try again
                </Button>
              ) : (
                <Button
                  size="sm"
                  loading={fixElsewhere.isPending}
                  onClick={() => fixElsewhere.mutate()}
                >
                  <WrenchIcon data-icon="inline-start" />
                  Fix the setup
                </Button>
              ))}
            {setup.output && (
              <CollapsibleTrigger
                render={<Button variant="ghost" size="sm" className="group" />}
              >
                Output
                <CaretDownIcon
                  data-icon="inline-end"
                  className="opacity-70 transition-transform group-data-panel-open:rotate-180"
                />
              </CollapsibleTrigger>
            )}
          </div>
          <CollapsibleContent>
            {/* It opens at its end, where a failure says what went wrong. */}
            <pre
              ref={(element) => {
                if (element) {
                  element.scrollTop = element.scrollHeight
                }
              }}
              className="mt-2 max-h-48 overflow-auto rounded-lg bg-muted px-3 py-2 text-xs [overflow-wrap:anywhere] whitespace-pre-wrap"
            >
              {asPlainText(setup.output ?? "")}
            </pre>
          </CollapsibleContent>
        </Collapsible>
      </Alert>
    </Banner>
  )
}

function FixedElsewhere({
  fixingIn,
  setupOf,
}: {
  fixingIn: string
  setupOf: string
}) {
  return (
    <Banner>
      <Alert>
        <WrenchIcon />
        <AlertTitle>{setupOf} is being fixed in another chat</AlertTitle>
        <AlertDescription>
          This chat goes on without what it installs.
        </AlertDescription>
        <div className="col-start-2 mt-2">
          <Link
            to="/chats/$chatId"
            params={{ chatId: fixingIn }}
            className={buttonVariants({ variant: "outline", size: "sm" })}
          >
            Open it
          </Link>
        </div>
      </Alert>
    </Banner>
  )
}

function Fixed({
  chatId,
  setupOf,
  proposing,
}: {
  chatId: string
  setupOf: string
  proposing: boolean
}) {
  const push = usePush(chatId)
  return (
    <Banner>
      <Alert className="*:[svg]:text-primary">
        <CheckCircleIcon />
        <AlertTitle>{setupOf} works now</AlertTitle>
        <AlertDescription>
          New chats get it once the fix is merged.
        </AlertDescription>
        {!proposing && (
          <div className="col-start-2 mt-2">
            <Button
              size="sm"
              loading={push.isPending}
              onClick={() => push.mutate(true)}
            >
              <GitPullRequestIcon data-icon="inline-start" />
              Open a pull request
            </Button>
          </div>
        )}
      </Alert>
    </Banner>
  )
}

function Working({ children }: { children: string }) {
  return (
    <Banner>
      <Alert>
        <CircleNotchIcon className="animate-spin motion-reduce:animate-none" />
        <AlertDescription className="text-foreground">
          {children}
        </AlertDescription>
      </Alert>
    </Banner>
  )
}

function Banner({ children }: { children: ReactNode }) {
  return (
    <div className="shrink-0 px-4 pb-2 md:px-5">
      <div className="mx-auto w-full max-w-[44rem]">{children}</div>
    </div>
  )
}

// The repository whose setup it is, by its folder in the nook's files, when it's one repository's.
function repositoryOf(scripts: readonly string[]): string | null {
  const [script] = scripts
  if (scripts.length !== 1 || !script.includes("/.agents/")) {
    return null
  }
  return script.slice(0, script.indexOf("/.agents/"))
}

// Terminal output as a terminal leaves it: without its color and cursor codes, and each line as its
// last carriage return left it, as a progress bar redraws one.
function asPlainText(output: string): string {
  return output
    .replace(terminalCodes, "")
    .split("\n")
    .map((line) => {
      const ended = line.replace(/\r+$/, "")
      return ended.slice(ended.lastIndexOf("\r") + 1)
    })
    .join("\n")
}

// ANSI's control sequences: ESC [, parameters, intermediates, and a final byte.
// eslint-disable-next-line no-control-regex -- the escape character is what they start with
const terminalCodes = /\x1b\[[0-?]*[ -/]*[@-~]/g
