export const HttpStatusCode = {
  Ok: 200,
  Created: 201,
  Accepted: 202,
  NoContent: 204,
  BadRequest: 400,
  Unauthorized: 401,
  Forbidden: 403,
  NotFound: 404,
  Conflict: 409,
  ContentTooLarge: 413,
  UnprocessableContent: 422,
  TooManyRequests: 429,
  InternalServerError: 500,
  NotImplemented: 501,
  ServiceUnavailable: 503,
  GatewayTimeout: 504,
} as const;

export function errorResponse(error: string, status: number): Response {
  return Response.json({ error }, { status });
}
