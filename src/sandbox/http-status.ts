export const HttpStatusCode = {
  Ok: 200,
  BadRequest: 400,
  Unauthorized: 401,
  NotFound: 404,
  UnprocessableContent: 422,
  InternalServerError: 500,
  ServiceUnavailable: 503,
  GatewayTimeout: 504,
} as const;
