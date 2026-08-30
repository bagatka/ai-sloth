const FLOW_TOKEN_BYTES = 32;
const CODE_VERIFIER_BYTES = 32;
const ENCRYPTION_NONCE_BYTES = 12;

export type ConnectionFlowSecrets = {
  state: string;
  stateHash: string;
  codeVerifier: string;
  codeChallenge: string;
};

export async function createConnectionFlowSecrets(): Promise<ConnectionFlowSecrets> {
  const state = randomBase64Url(FLOW_TOKEN_BYTES);
  const codeVerifier = randomBase64Url(CODE_VERIFIER_BYTES);
  return {
    state,
    stateHash: await hashBase64Url(state),
    codeVerifier,
    codeChallenge: await hashBase64Url(codeVerifier),
  };
}

export function hashConnectionState(state: string): Promise<string> {
  return hashBase64Url(state);
}

export async function encryptSecret(
  plaintext: string,
  encodedKey: string,
): Promise<string> {
  const key = await importEncryptionKey(encodedKey, ["encrypt"]);
  const nonce = crypto.getRandomValues(new Uint8Array(ENCRYPTION_NONCE_BYTES));
  const encrypted = await crypto.subtle.encrypt(
    { name: "AES-GCM", iv: nonce },
    key,
    new TextEncoder().encode(plaintext),
  );
  const value = new Uint8Array(nonce.length + encrypted.byteLength);
  value.set(nonce);
  value.set(new Uint8Array(encrypted), nonce.length);
  return encodeBase64Url(value);
}

export async function decryptSecret(
  encrypted: string,
  encodedKey: string,
): Promise<string> {
  const value = decodeBase64Url(encrypted);
  if (value.length <= ENCRYPTION_NONCE_BYTES) {
    throw new Error("Invalid encrypted GitHub credential");
  }
  const key = await importEncryptionKey(encodedKey, ["decrypt"]);
  const plaintext = await crypto.subtle.decrypt(
    { name: "AES-GCM", iv: value.slice(0, ENCRYPTION_NONCE_BYTES) },
    key,
    value.slice(ENCRYPTION_NONCE_BYTES),
  );
  return new TextDecoder().decode(plaintext);
}

export function isEncryptionKey(value: string): boolean {
  try {
    return decodeBase64Url(value).length === 32;
  } catch {
    return false;
  }
}

function randomBase64Url(bytes: number): string {
  return encodeBase64Url(crypto.getRandomValues(new Uint8Array(bytes)));
}

async function hashBase64Url(value: string): Promise<string> {
  const hash = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(value),
  );
  return encodeBase64Url(new Uint8Array(hash));
}

async function importEncryptionKey(
  encodedKey: string,
  usages: Array<"encrypt" | "decrypt">,
): Promise<CryptoKey> {
  const value = decodeBase64Url(encodedKey);
  if (value.length !== 32) {
    throw new Error("Invalid GitHub credential encryption key");
  }
  return crypto.subtle.importKey("raw", value, "AES-GCM", false, usages);
}

function encodeBase64Url(value: Uint8Array): string {
  let binary = "";
  for (const byte of value) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary)
    .replaceAll("+", "-")
    .replaceAll("/", "_")
    .replace(/=+$/, "");
}

function decodeBase64Url(value: string): Uint8Array {
  if (!/^[A-Za-z0-9_-]+$/.test(value)) {
    throw new Error("Invalid base64url value");
  }
  const standard = value.replaceAll("-", "+").replaceAll("_", "/");
  const padded = standard.padEnd(Math.ceil(standard.length / 4) * 4, "=");
  const binary = atob(padded);
  return Uint8Array.from(binary, (character) => character.charCodeAt(0));
}
