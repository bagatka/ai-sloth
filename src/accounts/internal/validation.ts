import type {
  LoginInput,
  RegisterInput,
} from "../authentication";
import {
  isValidNewPassword,
  isValidPasswordInput,
} from "./password";

const MAX_EMAIL_LENGTH = 254;

export type NormalizedRegistration = {
  email: string;
  normalizedEmail: string;
  password: string;
};

export type NormalizedLogin = {
  normalizedEmail: string;
  password: string;
};

export function normalizeRegistration(
  input: RegisterInput,
): NormalizedRegistration | null {
  const email = normalizeEmail(input.email);
  if (!email || !isValidNewPassword(input.password)) {
    return null;
  }

  return { ...email, password: input.password };
}

export function normalizeLogin(input: LoginInput): NormalizedLogin | null {
  const email = normalizeEmail(input.email);
  if (!email || !isValidPasswordInput(input.password)) {
    return null;
  }
  return { normalizedEmail: email.normalizedEmail, password: input.password };
}

function normalizeEmail(
  value: string,
): { email: string; normalizedEmail: string } | null {
  const email = value.trim().normalize("NFC");
  if (
    email.length === 0
    || email.length > MAX_EMAIL_LENGTH
    || /[\s\u0000-\u001f\u007f]/.test(email)
  ) {
    return null;
  }

  const separator = email.indexOf("@");
  if (
    separator < 1
    || separator !== email.lastIndexOf("@")
    || separator > 64
    || separator === email.length - 1
    || !email.slice(separator + 1).includes(".")
  ) {
    return null;
  }

  return { email, normalizedEmail: email.toLowerCase() };
}
