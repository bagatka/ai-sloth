export const HttpStatusCode = {
  Ok: 200,
  Created: 201,
  BadRequest: 400,
  Unauthorized: 401,
  NotFound: 404,
  Conflict: 409,
  ContentTooLarge: 413,
  UnprocessableContent: 422,
  InternalServerError: 500,
  ServiceUnavailable: 503,
  GatewayTimeout: 504,
} as const;

export function errorResponse(
  error: string,
  status: number,
  details?: string,
): Response {
  return Response.json(
    details ? { error, details } : { error },
    { status },
  );
}
