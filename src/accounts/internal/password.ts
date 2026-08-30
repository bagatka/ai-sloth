const ALGORITHM = "PBKDF2-HMAC-SHA256";
// OWASP's current PBKDF2-HMAC-SHA256 floor; stored per credential for upgrades.
const ITERATIONS = 600_000;
const SALT_BYTES = 16;
const HASH_BYTES = 32;
const MIN_PASSWORD_CHARACTERS = 12;
const MAX_PASSWORD_BYTES = 1024;

export type PasswordCredential = {
  algorithm: string;
  iterations: number;
  salt: string;
  hash: string;
};

export const DUMMY_PASSWORD_CREDENTIAL: PasswordCredential = {
  algorithm: ALGORITHM,
  iterations: ITERATIONS,
  salt: encodeBase64Url(new Uint8Array(SALT_BYTES)),
  hash: encodeBase64Url(new Uint8Array(HASH_BYTES)),
};

export function isValidNewPassword(password: string): boolean {
  return [...password].length >= MIN_PASSWORD_CHARACTERS
    && new TextEncoder().encode(password).byteLength <= MAX_PASSWORD_BYTES;
}

export function isValidPasswordInput(password: string): boolean {
  const length = new TextEncoder().encode(password).byteLength;
  return length > 0 && length <= MAX_PASSWORD_BYTES;
}

export async function hashPassword(
  password: string,
): Promise<PasswordCredential> {
  const salt = crypto.getRandomValues(new Uint8Array(SALT_BYTES));
  const hash = await derivePassword(password, salt, ITERATIONS);
  return {
    algorithm: ALGORITHM,
    iterations: ITERATIONS,
    salt: encodeBase64Url(salt),
    hash: encodeBase64Url(hash),
  };
}

export async function verifyPassword(
  password: string,
  credential: PasswordCredential,
): Promise<boolean> {
  if (credential.algorithm !== ALGORITHM || credential.iterations < 1) {
    return false;
  }

  const expected = decodeBase64Url(credential.hash);
  const actual = await derivePassword(
    password,
    decodeBase64Url(credential.salt),
    credential.iterations,
  );
  return constantTimeEqual(actual, expected);
}

async function derivePassword(
  password: string,
  salt: Uint8Array,
  iterations: number,
): Promise<Uint8Array> {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(password),
    "PBKDF2",
    false,
    ["deriveBits"],
  );
  const bits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", hash: "SHA-256", salt, iterations },
    key,
    HASH_BYTES * 8,
  );
  return new Uint8Array(bits);
}

function constantTimeEqual(left: Uint8Array, right: Uint8Array): boolean {
  let difference = left.length ^ right.length;
  const length = Math.max(left.length, right.length);
  for (let index = 0; index < length; index += 1) {
    difference |= (left[index] ?? 0) ^ (right[index] ?? 0);
  }
  return difference === 0;
}

function encodeBase64Url(value: Uint8Array): string {
  return btoa(String.fromCharCode(...value))
    .replaceAll("+", "-")
    .replaceAll("/", "_")
    .replace(/=+$/, "");
}

function decodeBase64Url(value: string): Uint8Array {
  const base64 = value.replaceAll("-", "+").replaceAll("_", "/");
  const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, "=");
  return Uint8Array.from(atob(padded), (character) => character.charCodeAt(0));
}
