const TOKEN_PREFIX = "asl_workspace_invite_";
const TOKEN_BYTES = 32;
const INVITATION_LIFETIME_MS = 7 * 24 * 60 * 60 * 1000;

export type NewInvitation = {
  token: string;
  tokenHash: string;
  createdAt: number;
  expiresAt: number;
};

export async function createInvitationToken(): Promise<NewInvitation> {
  const random = crypto.getRandomValues(new Uint8Array(TOKEN_BYTES));
  const token = TOKEN_PREFIX + encodeBase64Url(random);
  const createdAt = Date.now();
  return {
    token,
    tokenHash: await hashInvitationToken(token),
    createdAt,
    expiresAt: createdAt + INVITATION_LIFETIME_MS,
  };
}

export function isInvitationToken(value: string): boolean {
  return /^asl_workspace_invite_[A-Za-z0-9_-]{43}$/.test(value);
}

export async function hashInvitationToken(token: string): Promise<string> {
  const hash = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(token),
  );
  return encodeBase64Url(new Uint8Array(hash));
}

function encodeBase64Url(value: Uint8Array): string {
  return btoa(String.fromCharCode(...value))
    .replaceAll("+", "-")
    .replaceAll("/", "_")
    .replace(/=+$/, "");
}
