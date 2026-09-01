import type { MiddlewareHandler } from "hono";
import type { ApiEnvironment } from "../environment";
import {
  readJsonBody,
  RequestBodyTooLargeError,
} from "./body";
import { errorResponse, HttpStatusCode } from "./response";

export const readRequestBody: MiddlewareHandler<ApiEnvironment> = async (
  context,
  next,
) => {
  try {
    context.set("requestBody", await readJsonBody(context.req.raw));
  } catch (error) {
    return errorResponse(
      error instanceof RequestBodyTooLargeError
        ? "Request body is too large"
        : "Invalid JSON body",
      error instanceof RequestBodyTooLargeError
        ? HttpStatusCode.ContentTooLarge
        : HttpStatusCode.BadRequest,
    );
  }

  await next();
};
