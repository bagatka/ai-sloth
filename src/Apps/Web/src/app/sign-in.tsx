import * as React from "react"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { useNavigate, useSearch } from "@tanstack/react-router"

import { Alert, AlertDescription } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldSeparator,
} from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { ApiError, answer, api } from "./api"
import { apiUrl, deviceName } from "./host"
import { discovery } from "./queries"

// Signing in: through the host's identity provider when it has one, or with a code. A code is the
// host's setup code (it prints one until its first person signs up), a link code from another device
// the person is signed in on, or an invite. The session ends up in the browser's cookie either way.

const signInErrors: Record<string, string> = {
  access_denied: "Sign-in was cancelled.",
  server_error: "The host couldn't finish signing you in. Try again.",
}

export function SignInPage() {
  const { return_to: returnTo, sign_in_error: signInError } = useSearch({
    from: "/sign-in",
  })
  const host = useQuery(discovery)
  const provider = host.data?.signIn.provider ?? null
  const name = host.data?.name ?? "AiSloth"

  return (
    <main className="flex min-h-svh flex-col items-center justify-center bg-background px-4 py-10">
      <title>{`Sign in – ${name}`}</title>
      <div className="flex w-full max-w-sm flex-col gap-7">
        <div className="flex flex-col gap-1.5">
          <p className="text-label font-semibold text-primary">AiSloth</p>
          <h1 className="text-2xl font-semibold tracking-tight text-balance">
            Sign in to {name}
          </h1>
          <p className="text-read text-muted-foreground">
            Follow your agents and steer them from here.
          </p>
        </div>
        {signInError && (
          <Alert variant="destructive">
            <AlertDescription>
              {signInErrors[signInError] ??
                `Sign-in didn't finish (${signInError}). Try again.`}
            </AlertDescription>
          </Alert>
        )}
        {host.isError && (
          <Alert variant="destructive">
            <AlertDescription>
              Can't reach the host. Check your connection, then reload.
            </AlertDescription>
          </Alert>
        )}
        {provider && (
          <>
            <Button
              size="lg"
              onClick={() => {
                const start = new URL("sign-in/browser", apiUrl)
                start.searchParams.set("device", deviceName())
                if (returnTo) {
                  start.searchParams.set("return_to", returnTo)
                }
                location.assign(start)
              }}
            >
              Continue with {provider}
            </Button>
            <FieldSeparator>or use a code</FieldSeparator>
          </>
        )}
        {/* Without a provider, a code is the main way in. */}
        <CodeSignIn returnTo={returnTo} primary={!provider} />
      </div>
    </main>
  )
}

function CodeSignIn({
  returnTo,
  primary,
}: {
  returnTo: string | undefined
  primary: boolean
}) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [code, setCode] = React.useState("")
  const [name, setName] = React.useState("")
  // Someone new gives their name; the host says when it needs one.
  const [newcomer, setNewcomer] = React.useState(false)
  const signIn = useMutation({
    mutationFn: () =>
      answer(
        api.POST("/sign-in/browser/code", {
          body: {
            code: code.trim(),
            device: deviceName(),
            name: newcomer ? name.trim() : null,
          },
        })
      ),
    onSuccess: () => {
      queryClient.clear()
      const chat = /^chats\/([0-9a-f-]+)$/.exec(returnTo ?? "")
      void (chat
        ? navigate({ to: "/chats/$chatId", params: { chatId: chat[1] } })
        : navigate({ to: "/" }))
    },
    onError: (error) => {
      // The host asks someone new for their name: the field opens, without the refusal.
      if (error instanceof ApiError && error.fields.name && !newcomer) {
        setNewcomer(true)
        signIn.reset()
      }
    },
  })
  const error = signIn.error instanceof ApiError ? signIn.error : null
  const needsName = newcomer && name.trim() === ""

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault()
        if (code.trim() && !needsName) {
          signIn.mutate()
        }
      }}
    >
      <FieldGroup>
        <Field data-invalid={(error && !error.fields.name) || undefined}>
          <FieldLabel htmlFor="code">Code</FieldLabel>
          <Input
            id="code"
            value={code}
            onChange={(event) => setCode(event.target.value)}
            autoComplete="one-time-code"
            autoCapitalize="characters"
            spellCheck={false}
            className="font-mono tracking-wider"
            aria-invalid={(error && !error.fields.name) || undefined}
          />
          {error && !error.fields.name ? (
            <FieldError>{error.message}</FieldError>
          ) : (
            <FieldDescription>
              A setup code from the host, a link code from a device you're
              signed in on, or an invite.
            </FieldDescription>
          )}
        </Field>
        {newcomer && (
          <Field>
            <FieldLabel htmlFor="name">Your name</FieldLabel>
            <Input
              id="name"
              value={name}
              onChange={(event) => setName(event.target.value)}
              autoComplete="name"
              autoFocus
            />
            {error?.fields.name ? (
              <FieldError>{error.fields.name[0]}</FieldError>
            ) : (
              <FieldDescription>
                You're new here: this is how others will see you.
              </FieldDescription>
            )}
          </Field>
        )}
        {signIn.isError && !error && (
          <Alert variant="destructive">
            <AlertDescription>
              Can't reach the host. Check your connection and try again.
            </AlertDescription>
          </Alert>
        )}
        <Button
          type="submit"
          variant={primary ? "default" : "outline"}
          loading={signIn.isPending}
          disabled={!code.trim() || needsName}
        >
          Sign in
        </Button>
      </FieldGroup>
    </form>
  )
}
