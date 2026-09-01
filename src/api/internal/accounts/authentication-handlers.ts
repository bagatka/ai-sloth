import type { Handler } from "hono";
import type { ApiEnvironment } from "../environment";
import { errorResponse, HttpStatusCode } from "../http/response";
import {
  parseLoginRequest,
  parseRegisterRequest,
} from "./request";
import {
  accountFailureResponse,
  accountProfileResponse,
  accountSessionResponse,
} from "./response";

export const register: Handler<ApiEnvironment> = async (context) => {
  const input = parseRegisterRequest(context.get("requestBody"));
  if (!input) {
    return errorResponse(
      "Expected email and password",
      HttpStatusCode.BadRequest,
    );
  }

  const outcome = await context.var.accounts.register(input);
  return outcome.ok
    ? accountSessionResponse(outcome.value, HttpStatusCode.Created)
    : accountFailureResponse(outcome.code);
};

export const login: Handler<ApiEnvironment> = async (context) => {
  const input = parseLoginRequest(context.get("requestBody"));
  if (!input) {
    return errorResponse(
      "Expected email and password",
      HttpStatusCode.BadRequest,
    );
  }

  const outcome = await context.var.accounts.login(input);
  return outcome.ok
    ? accountSessionResponse(outcome.value, HttpStatusCode.Ok)
    : accountFailureResponse(outcome.code);
};

export const logout: Handler<ApiEnvironment> = async (context) => {
  const outcome = await context.var.accounts.logout(context.var.accountToken);
  return outcome.ok
    ? new Response(null, { status: 204 })
    : accountFailureResponse(outcome.code);
};

export const currentAccount: Handler<ApiEnvironment> = (context) =>
  accountProfileResponse(context.var.account);
