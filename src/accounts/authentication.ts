export type Actor = {
  userId: string;
};

export type User = {
  id: string;
  email: string;
};

export type AccountProfile = {
  actor: Actor;
  user: User;
};

export type AccountSession = AccountProfile & {
  sessionToken: string;
  expiresAt: string;
};

export type RegisterInput = {
  email: string;
  password: string;
};

export type LoginInput = {
  email: string;
  password: string;
};

export type AccountFailureCode =
  | "invalid_input"
  | "email_taken"
  | "invalid_credentials"
  | "invalid_session"
  | "internal_error";

export type AccountOutcome<T> =
  | { ok: true; value: T }
  | { ok: false; code: AccountFailureCode };

export type AccountOperations = {
  register(input: RegisterInput): Promise<AccountOutcome<AccountSession>>;
  login(input: LoginInput): Promise<AccountOutcome<AccountSession>>;
  authenticate(token: string): Promise<AccountOutcome<AccountProfile>>;
  logout(token: string): Promise<AccountOutcome<undefined>>;
};
