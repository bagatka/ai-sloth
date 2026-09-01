import { bindAccounts } from "@ai-sloth/accounts";
import type { MiddlewareHandler } from "hono";
import type { ApiEnvironment } from "../environment";

export const withAccounts: MiddlewareHandler<ApiEnvironment> = async (
  context,
  next,
) => {
  context.set("accounts", bindAccounts(context.env));
  await next();
};
