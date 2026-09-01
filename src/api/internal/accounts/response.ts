import type {
  AccountFailureCode,
  AccountProfile,
  AccountSession,
} from "@ai-sloth/accounts";
import { errorResponse, HttpStatusCode } from "../http/response";

export type AccountProfileResponse = {
  user: AccountProfile["user"];
};

export type AccountSessionResponse = AccountProfileResponse & {
  sessionToken: string;
  expiresAt: string;
};

export function accountSessionResponse(
  session: AccountSession,
  status: number,
): Response {
  const response: AccountSessionResponse = {
    user: session.user,
    sessionToken: session.sessionToken,
    expiresAt: session.expiresAt,
  };
  return Response.json(response, {
    status,
    headers: { "Cache-Control": "no-store" },
  });
}

export function accountProfileResponse(profile: AccountProfile): Response {
  const response: AccountProfileResponse = { user: profile.user };
  return Response.json(response, {
    headers: { "Cache-Control": "no-store" },
  });
}

export function accountFailureResponse(code: AccountFailureCode): Response {
  switch (code) {
    case "invalid_input":
      return errorResponse("Invalid account input", HttpStatusCode.BadRequest);
    case "email_taken":
      return errorResponse(
        "Email is already registered",
        HttpStatusCode.Conflict,
      );
    case "invalid_credentials":
      return errorResponse(
        "Invalid email or password",
        HttpStatusCode.Unauthorized,
      );
    case "invalid_session":
      return errorResponse("Unauthorized", HttpStatusCode.Unauthorized);
    case "internal_error":
      return errorResponse(
        "Account operation failed",
        HttpStatusCode.InternalServerError,
      );
  }
}
