import * as React from "react"
import { FileTextIcon, ImageIcon, XIcon } from "@phosphor-icons/react"

import {
  Attachment,
  AttachmentAction,
  AttachmentActions,
  AttachmentContent,
  AttachmentDescription,
  AttachmentGroup,
  AttachmentMedia,
  AttachmentTitle,
} from "@/components/ui/attachment"
import { Avatar, AvatarFallback } from "@/components/ui/avatar"
import { Bubble, BubbleContent, BubbleGroup } from "@/components/ui/bubble"
import { Marker, MarkerContent } from "@/components/ui/marker"
import {
  Message,
  MessageAvatar,
  MessageContent,
  MessageFooter,
  MessageGroup,
  MessageHeader,
} from "@/components/ui/message"
import {
  MessageScroller,
  MessageScrollerButton,
  MessageScrollerContent,
  MessageScrollerItem,
  MessageScrollerProvider,
  MessageScrollerViewport,
} from "@/components/ui/message-scroller"
import {
  Questionnaire,
  QuestionnaireActions,
  QuestionnaireChoice,
  QuestionnaireChoices,
  QuestionnaireDescription,
  QuestionnaireError,
  QuestionnaireInput,
  QuestionnaireItem,
  QuestionnaireNext,
  QuestionnairePrevious,
  QuestionnaireProgress,
  QuestionnaireSubmit,
  QuestionnaireTitle,
} from "@/components/ui/questionnaire"
import { toast } from "@/components/ui/toast"
import { AgentWorking } from "@/components/agent-working"
import { Section, Specimen } from "./section"

// The sample turn started 72 seconds before the page opened.
const turnStarted = Date.now() - 72_000

type Row =
  | { id: string; kind: "person"; text: string; attachments?: string[] }
  | { id: string; kind: "agent"; text: React.ReactNode }
  | { id: string; kind: "marker"; text: string }
  | { id: string; kind: "working" }

const transcript: Row[] = [
  {
    id: "1",
    kind: "person",
    text: "The checkout test fails about one run in ten on CI. Find out why and fix it.",
    attachments: ["ci-run-8812.log"],
  },
  {
    id: "2",
    kind: "agent",
    text: "The test waits for the payment iframe with a fixed 2 second timeout, and CI is slower when the cache is cold. I’ll wait for the iframe’s ready message instead.",
  },
  {
    id: "3",
    kind: "agent",
    text: (
      <>
        Changed <code>checkout.test.ts</code> and the payment fixture. The test
        passed 200 runs in a row in the nook.
      </>
    ),
  },
  { id: "4", kind: "marker", text: "Turn ended after 3 minutes" },
  { id: "5", kind: "person", text: "Nice. Push it and open a pull request." },
  {
    id: "6",
    kind: "agent",
    text: (
      <>
        Pushed to <code>ada/fix-flaky-checkout</code> and opened pull request
        #482.
      </>
    ),
  },
  { id: "7", kind: "marker", text: "Turn ended after 20 seconds" },
  {
    id: "8",
    kind: "person",
    text: "Fix the other two flaky tests you found too.",
  },
  { id: "9", kind: "working" },
]

const questions = [
  {
    name: "database",
    required: true,
    title: "Which database should the tests use?",
    description: "The repository has settings for both.",
    choices: [
      { value: "postgres", label: "PostgreSQL in Docker" },
      { value: "sqlite", label: "SQLite in memory" },
    ],
  },
  {
    name: "scope",
    required: false,
    title: "Fix the other flaky tests too?",
    description: "I found two more that wait with fixed timeouts.",
    choices: [
      { value: "yes", label: "Yes, in this pull request" },
      { value: "later", label: "No, in another chat" },
    ],
  },
] as const

export function Chat() {
  return (
    <Section id="chat" title="Chat">
      <Specimen name="Message scroller with messages, bubbles, markers" wide>
        <div className="h-[28rem] w-full">
          <MessageScrollerProvider>
            <MessageScroller>
              <MessageScrollerViewport>
                <MessageScrollerContent className="p-1">
                  {transcript.map((row) => (
                    <MessageScrollerItem
                      key={row.id}
                      messageId={row.id}
                      scrollAnchor={row.kind === "person"}
                    >
                      <TranscriptRow row={row} />
                    </MessageScrollerItem>
                  ))}
                </MessageScrollerContent>
              </MessageScrollerViewport>
              <MessageScrollerButton />
            </MessageScroller>
          </MessageScrollerProvider>
        </div>
      </Specimen>
      <Specimen name="Bubble variants">
        <BubbleGroup className="w-full">
          {(
            [
              "default",
              "secondary",
              "muted",
              "tinted",
              "outline",
              "destructive",
            ] as const
          ).map((variant) => (
            <Bubble key={variant} variant={variant}>
              <BubbleContent>{variant}</BubbleContent>
            </Bubble>
          ))}
        </BubbleGroup>
      </Specimen>
      <Specimen name="Attachments">
        <AttachmentGroup>
          <Attachment>
            <AttachmentMedia>
              <FileTextIcon />
            </AttachmentMedia>
            <AttachmentContent>
              <AttachmentTitle>ci-run-8812.log</AttachmentTitle>
              <AttachmentDescription>48 KB</AttachmentDescription>
            </AttachmentContent>
            <AttachmentActions>
              <AttachmentAction aria-label="Remove">
                <XIcon />
              </AttachmentAction>
            </AttachmentActions>
          </Attachment>
          <Attachment state="uploading">
            <AttachmentMedia>
              <ImageIcon />
            </AttachmentMedia>
            <AttachmentContent>
              <AttachmentTitle>checkout-error.png</AttachmentTitle>
              <AttachmentDescription>Uploading</AttachmentDescription>
            </AttachmentContent>
          </Attachment>
        </AttachmentGroup>
      </Specimen>
      <Specimen name="Questionnaire: the agent asks" wide>
        <AgentQuestions />
      </Specimen>
      <Specimen name="Marker variants">
        <div className="flex w-full flex-col gap-4">
          <Marker>
            <MarkerContent>Ada joined the chat</MarkerContent>
          </Marker>
          <Marker variant="separator">
            <MarkerContent>Today</MarkerContent>
          </Marker>
          <Marker variant="border">
            <MarkerContent>Steered: also update the README</MarkerContent>
          </Marker>
        </div>
      </Specimen>
    </Section>
  )
}

function TranscriptRow({ row }: { row: Row }) {
  if (row.kind === "working") {
    return <AgentWorking since={turnStarted} />
  }

  if (row.kind === "marker") {
    return (
      <Marker variant="separator">
        <MarkerContent>{row.text}</MarkerContent>
      </Marker>
    )
  }

  if (row.kind === "person") {
    return (
      <Message align="end">
        <MessageContent>
          {row.attachments?.map((name) => (
            <Attachment key={name} size="sm">
              <AttachmentMedia>
                <FileTextIcon />
              </AttachmentMedia>
              <AttachmentContent>
                <AttachmentTitle>{name}</AttachmentTitle>
              </AttachmentContent>
            </Attachment>
          ))}
          <Bubble variant="secondary">
            <BubbleContent>{row.text}</BubbleContent>
          </Bubble>
        </MessageContent>
      </Message>
    )
  }

  return (
    <MessageGroup>
      <Message>
        <MessageAvatar>
          <Avatar size="sm">
            <AvatarFallback>CC</AvatarFallback>
          </Avatar>
        </MessageAvatar>
        <MessageContent>
          <MessageHeader>Claude Code</MessageHeader>
          <Bubble variant="ghost">
            <BubbleContent>{row.text}</BubbleContent>
          </Bubble>
          <MessageFooter>Sonnet</MessageFooter>
        </MessageContent>
      </Message>
    </MessageGroup>
  )
}

function AgentQuestions() {
  const onSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const answers = new FormData(event.currentTarget)
    toast.add({
      title: "Answered the agent",
      description: `Database: ${String(answers.get("database"))}`,
    })
  }

  return (
    <Questionnaire items={questions} onSubmit={onSubmit} className="w-full">
      <QuestionnaireProgress />
      {questions.map((question) => (
        <QuestionnaireItem
          key={question.name}
          name={question.name}
          required={question.required}
        >
          <QuestionnaireTitle>{question.title}</QuestionnaireTitle>
          <QuestionnaireDescription>
            {question.description}
          </QuestionnaireDescription>
          <QuestionnaireChoices>
            {question.choices.map((choice) => (
              <QuestionnaireChoice key={choice.value} value={choice.value}>
                {choice.label}
              </QuestionnaireChoice>
            ))}
            <QuestionnaireInput
              aria-label="Something else"
              placeholder="Something else"
            />
          </QuestionnaireChoices>
          <QuestionnaireError />
        </QuestionnaireItem>
      ))}
      <QuestionnaireActions>
        <QuestionnairePrevious />
        <QuestionnaireNext />
        <QuestionnaireSubmit />
      </QuestionnaireActions>
    </Questionnaire>
  )
}
