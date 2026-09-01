export type Credentials = {
  email: string
  password: string
}

export type Account = {
  id: string
  email: string
}

export type AuthenticationOutcome = { ok: true } | { ok: false; error: string }

export type AuthenticatedRequest = (
  path: string,
  init?: RequestInit
) => Promise<Response>

export type Authentication = {
  authenticated: boolean
  account: Account | null
  pending: boolean
  request: AuthenticatedRequest
  register(credentials: Credentials): Promise<AuthenticationOutcome>
  signIn(credentials: Credentials): Promise<AuthenticationOutcome>
  signOut(): Promise<AuthenticationOutcome>
}

export { useAuthentication } from "./internal/use-authentication"
