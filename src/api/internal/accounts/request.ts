import type {
  LoginInput,
  RegisterInput,
} from "@ai-sloth/accounts";

export function parseRegisterRequest(value: unknown): RegisterInput | null {
  if (!isObject(value)) {
    return null;
  }
  const { email, password } = value;
  if (typeof email !== "string" || typeof password !== "string") {
    return null;
  }
  return { email, password };
}

export function parseLoginRequest(value: unknown): LoginInput | null {
  if (!isObject(value)) {
    return null;
  }
  const { email, password } = value;
  if (typeof email !== "string" || typeof password !== "string") {
    return null;
  }
  return { email, password };
}

function isObject(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}
