import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import { Button } from "@/components/ui/button"
import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from "@/components/ui/field"
import { Skeleton } from "@/components/ui/skeleton"
import { Textarea } from "@/components/ui/textarea"
import { toast } from "@/components/ui/toast"
import { answer, api, messageOf, type Schemas } from "../api"
import { instructions as instructionsQuery } from "../queries"
import { Section } from "./section"

// Instructions: what every agent follows, whatever its harness, from when it starts: the
// workspace's, for everyone's chats there, and the person's own, for the chats they start anywhere.

type Workspace = Schemas["WorkspaceSummary"]

export function InstructionsSection({ workspace }: { workspace: Workspace }) {
  const read = useQuery(instructionsQuery(workspace.id))
  const writer = workspace.access !== "Read"

  return (
    <Section
      id="instructions"
      title="Instructions"
      description="What every agent follows, whatever its harness, in Markdown. Agents that start afterwards follow changes; running ones keep theirs."
    >
      {read.isPending ? (
        <Skeleton className="h-32 w-full rounded-xl" />
      ) : read.isError ? (
        <p className="text-label text-destructive">{messageOf(read.error)}</p>
      ) : (
        <>
          <InstructionsField
            id="workspace-instructions"
            label={`For everyone's chats in ${workspace.name}`}
            saved={read.data.workspace}
            readOnly={!writer}
            save={(text) =>
              answer(
                api.PUT("/workspaces/{workspaceId}/instructions", {
                  params: { path: { workspaceId: workspace.id } },
                  body: { text },
                })
              )
            }
          />
          <InstructionsField
            id="personal-instructions"
            label="For the chats you start, in every workspace"
            saved={read.data.personal}
            readOnly={false}
            save={(text) =>
              answer(api.PUT("/instructions", { body: { text } }))
            }
          />
        </>
      )}
    </Section>
  )
}

function InstructionsField({
  id,
  label,
  saved,
  readOnly,
  save,
}: {
  id: string
  label: string
  saved: string
  readOnly: boolean
  save: (text: string) => Promise<unknown>
}) {
  const queryClient = useQueryClient()
  // What's written here, until it's saved; the saved text otherwise.
  const [draft, setDraft] = React.useState<string | null>(null)
  const text = draft ?? saved
  const write = useMutation({
    mutationFn: () => save(text),
    onSuccess: async () => {
      // Someone's own instructions show in every workspace's settings.
      await queryClient.invalidateQueries({
        predicate: (query) =>
          query.queryKey[0] === "workspaces" &&
          query.queryKey[2] === "instructions",
      })
      setDraft(null)
      toast.add({ type: "success", title: "Instructions saved" })
    },
  })

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault()
        write.mutate()
      }}
    >
      <Field data-invalid={write.isError || undefined}>
        <FieldLabel htmlFor={id}>{label}</FieldLabel>
        <Textarea
          id={id}
          value={text}
          readOnly={readOnly}
          onChange={(event) => setDraft(event.target.value)}
          placeholder={
            readOnly
              ? "None"
              : "Such as: run the tests before saying you're done."
          }
          className="min-h-28"
          aria-invalid={write.isError || undefined}
        />
        {write.isError ? (
          <FieldError>{messageOf(write.error)}</FieldError>
        ) : readOnly ? (
          <FieldDescription>
            People who work in it may change them.
          </FieldDescription>
        ) : null}
      </Field>
      {!readOnly && draft !== null && draft !== saved && (
        <div className="mt-2 flex justify-end gap-2">
          <Button variant="ghost" size="sm" onClick={() => setDraft(null)}>
            Discard
          </Button>
          <Button type="submit" size="sm" loading={write.isPending}>
            Save
          </Button>
        </div>
      )}
    </form>
  )
}
