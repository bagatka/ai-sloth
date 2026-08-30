import { useState } from "react"
import slothLogo320 from "@/assets/ai-sloth-logo-320.webp"
import slothLogo640 from "@/assets/ai-sloth-logo-640.webp"
import type { Authentication, Credentials } from "@/authentication"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldSeparator,
} from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { cn } from "@/lib/utils"

type AuthenticationMode = "register" | "signIn"

type AuthenticationFormProps = React.ComponentProps<"div"> & {
  authentication: Authentication
}

export function AuthenticationForm({
  authentication,
  className,
  ...props
}: AuthenticationFormProps) {
  const [mode, setMode] = useState<AuthenticationMode>("signIn")
  const [error, setError] = useState<string | null>(null)
  const registering = mode === "register"

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (authentication.pending) return

    setError(null)
    const data = new FormData(event.currentTarget)
    const credentials: Credentials = {
      email: String(data.get("email") ?? ""),
      password: String(data.get("password") ?? ""),
    }
    const outcome = await (registering
      ? authentication.register(credentials)
      : authentication.signIn(credentials))
    if (!outcome.ok) {
      setError(outcome.error)
    }
  }

  function changeMode() {
    setError(null)
    setMode(registering ? "signIn" : "register")
  }

  return (
    <div className={cn("flex flex-col gap-6", className)} {...props}>
      <Card className="overflow-hidden p-0">
        <CardContent className="grid p-0 md:grid-cols-2">
          <form key={mode} className="p-6 md:p-8" onSubmit={submit}>
            <FieldGroup>
              <div className="flex flex-col items-center gap-2 text-center">
                <h1 className="font-heading text-2xl font-semibold">
                  {registering ? "Create your account" : "Welcome back"}
                </h1>
                <p className="text-balance text-muted-foreground">
                  {registering
                    ? "Register with your email and password"
                    : "Sign in to your AI Sloth account"}
                </p>
              </div>

              <Field>
                <FieldLabel htmlFor="email">Email</FieldLabel>
                <Input
                  id="email"
                  name="email"
                  type="email"
                  autoComplete="email"
                  placeholder="you@example.com"
                  maxLength={254}
                  disabled={authentication.pending}
                  required
                />
              </Field>

              <Field>
                <FieldLabel htmlFor="password">Password</FieldLabel>
                <Input
                  id="password"
                  name="password"
                  type="password"
                  autoComplete={
                    registering ? "new-password" : "current-password"
                  }
                  minLength={registering ? 12 : undefined}
                  disabled={authentication.pending}
                  required
                />
                <FieldDescription
                  className={registering ? undefined : "invisible"}
                  aria-hidden={!registering}
                >
                  Use at least 12 characters.
                </FieldDescription>
              </Field>

              {error && <FieldError>{error}</FieldError>}

              <Field>
                <Button type="submit" disabled={authentication.pending}>
                  {authentication.pending
                    ? registering
                      ? "Creating account…"
                      : "Signing in…"
                    : registering
                      ? "Create account"
                      : "Sign in"}
                </Button>
              </Field>

              <FieldSeparator className="*:data-[slot=field-separator-content]:bg-card">
                Or continue with
              </FieldSeparator>

              <Field className="grid gap-3 sm:grid-cols-2">
                <Button variant="outline" type="button" disabled>
                  <GitHubIcon />
                  GitHub
                </Button>
                <Button variant="outline" type="button" disabled>
                  <GoogleIcon />
                  Google
                </Button>
              </Field>

              <p className="text-center text-sm text-muted-foreground">
                {registering ? "Already have an account?" : "Need an account?"}{" "}
                <Button
                  className="h-auto p-0"
                  type="button"
                  variant="link"
                  onClick={changeMode}
                  disabled={authentication.pending}
                >
                  {registering ? "Sign in" : "Register"}
                </Button>
              </p>
            </FieldGroup>
          </form>

          <aside className="hidden flex-col justify-between bg-primary p-8 text-primary-foreground md:flex">
            <span className="font-heading text-lg font-semibold">AI Sloth</span>
            <img
              className="mx-auto w-full max-w-64"
              src={slothLogo320}
              srcSet={`${slothLogo320} 320w, ${slothLogo640} 640w`}
              sizes="256px"
              width="640"
              height="640"
              alt=""
              loading="lazy"
              decoding="async"
              fetchPriority="low"
            />
            <p className="text-sm leading-relaxed text-balance text-primary-foreground/80">
              Disposable cloud development environments built for AI coding
              agents.
            </p>
          </aside>
        </CardContent>
      </Card>
    </div>
  )
}

function GitHubIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24">
      <path
        d="M12 2C6.477 2 2 6.484 2 12.017c0 4.425 2.865 8.18 6.839 9.504.5.092.682-.217.682-.483 0-.237-.008-.867-.013-1.702-2.782.605-3.369-1.343-3.369-1.343-.454-1.158-1.11-1.466-1.11-1.466-.908-.62.069-.608.069-.608 1.003.071 1.531 1.032 1.531 1.032.892 1.53 2.341 1.088 2.91.832.092-.647.35-1.088.636-1.338-2.221-.253-4.555-1.112-4.555-4.945 0-1.092.39-1.984 1.03-2.683-.103-.253-.446-1.27.098-2.647 0 0 .84-.269 2.75 1.025A9.56 9.56 0 0 1 12 6.862a9.55 9.55 0 0 1 2.504.337c1.909-1.294 2.748-1.025 2.748-1.025.546 1.377.203 2.394.1 2.647.64.699 1.028 1.591 1.028 2.683 0 3.842-2.337 4.687-4.566 4.935.359.31.679.923.679 1.86 0 1.343-.012 2.427-.012 2.758 0 .268.18.58.688.482A10.02 10.02 0 0 0 22 12.017C22 6.484 17.522 2 12 2Z"
        fill="currentColor"
      />
    </svg>
  )
}

function GoogleIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24">
      <path
        d="M12.48 10.92v3.28h7.84c-.24 1.84-.853 3.187-1.787 4.133-1.147 1.147-2.933 2.4-6.053 2.4-4.827 0-8.6-3.893-8.6-8.72s3.773-8.72 8.6-8.72c2.6 0 4.507 1.027 5.907 2.347l2.307-2.307C18.747 1.44 16.133 0 12.48 0 5.867 0 .307 5.387.307 12s5.56 12 12.173 12c3.573 0 6.267-1.173 8.373-3.36 2.16-2.16 2.84-5.213 2.84-7.667 0-.76-.053-1.467-.173-2.053H12.48z"
        fill="currentColor"
      />
    </svg>
  )
}
