import * as React from "react"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { useNavigate } from "@tanstack/react-router"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { toast } from "@/components/ui/toast"
import { answer, api, messageOf } from "./api"
import { workspaces } from "./queries"
import { chooseWorkspace } from "./workspace"

// Joining a workspace someone was invited to, with the invite's code, when they already use AiSloth
// on this host; someone new signs in with the code instead.
export function JoinWorkspace({
  open,
  onOpenChange,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [code, setCode] = React.useState("")
  const join = useMutation({
    mutationFn: () =>
      answer(api.POST("/invites/accept", { body: { code: code.trim() } })),
    onSuccess: async (resource) => {
      onOpenChange(false)
      if (resource.kind === "Workspace") {
        await queryClient.invalidateQueries({ queryKey: workspaces.queryKey })
        chooseWorkspace(resource.id)
        void navigate({ to: "/" })
        toast.add({ type: "success", title: "You joined the workspace" })
      } else {
        // Not handled: finding the chat of a nook someone was invited to; its link opens it.
        toast.add({
          type: "success",
          title: "You can see the chat now",
          description: "Open the link to it you were given.",
        })
      }
    },
  })

  return (
    <Dialog
      open={open}
      onOpenChange={(opening) => {
        onOpenChange(opening)
        if (opening) {
          setCode("")
          join.reset()
        }
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Join a workspace</DialogTitle>
          <DialogDescription>
            With the invite someone there gave you.
          </DialogDescription>
        </DialogHeader>
        <form
          className="contents"
          onSubmit={(event) => {
            event.preventDefault()
            if (code.trim()) {
              join.mutate()
            }
          }}
        >
          <Field data-invalid={join.isError || undefined}>
            <FieldLabel htmlFor="invite-code">Invite</FieldLabel>
            <Input
              id="invite-code"
              value={code}
              onChange={(event) => setCode(event.target.value)}
              autoCapitalize="characters"
              spellCheck={false}
              className="font-mono tracking-wider"
              aria-invalid={join.isError || undefined}
            />
            {join.isError ? (
              <FieldError>{messageOf(join.error)}</FieldError>
            ) : (
              <FieldDescription>It works once, within a week.</FieldDescription>
            )}
          </Field>
          <DialogFooter>
            <Button
              type="submit"
              loading={join.isPending}
              disabled={!code.trim()}
            >
              Join
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
