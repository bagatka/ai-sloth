# Sdk

General-purpose clients for third-party APIs that lack a usable official .NET SDK. Every
project here could be copied into another product unchanged.

## Before adding a client

Check for an official, maintained .NET SDK. If one exists, use it directly from the module that
needs it. Write a client here only when there isn't one, or when the official one is unusable,
and record why in the client's README.

## Rules

- **Naming and dependencies.**
  - Name the project `Company.Sdk.<Vendor>`.
  - It references only .NET, approved packages, and `Company.Platform`. Never `Company.Product.*`.
- **Vendor-shaped.**
  - Types and methods mirror the vendor's API and vocabulary.
  - Product concepts never appear here. The module that uses the client does the translation.
- **Minimal surface.** Cover only the endpoints a product actually uses. Add more when needed.
- **Registration.** Expose one client class (or a few, one per vendor API area), registered with
  one extension: `services.Add<Vendor>Client(...)`.
- **HTTP and resilience.**
  - Use a typed `HttpClient` through `IHttpClientFactory`, with the standard resilience handler.
  - Nothing outside the client retries vendor calls.
  - Retry only idempotent requests, or requests carrying a vendor idempotency key.
- **Options.** An options class, bound by the caller and validated at startup. Secrets come from
  options, never from code.
- **Calls.**
  - `CancellationToken` on every method.
  - No static state, and no singletons holding per-request data.
- **Errors.**
  - Expected vendor outcomes (not found, rejected input, conflict) return `Result<T>` with codes
    `<vendor>.<reason>`.
  - Transport failures that survive retries throw.
- **JSON.** Use System.Text.Json with a source-generated `JsonSerializerContext` per client,
  matching the vendor's naming policy.
- **Webhooks.**
  - The client provides signature verification and payload parsing as pure functions.
  - The gateway receives the webhook and calls the owning module with a system actor.
- **Logging.** Never log request or response bodies that may contain secrets or personal data.

## Tests

- Test against recorded vendor responses, using a fake `HttpMessageHandler`.
- Live smoke tests are optional. They are opt-in through an environment variable and never run
  in CI by default.

## Each client's README

Every client has a short README covering:

- a link to the vendor's docs and the API version used;
- the endpoints covered;
- the auth method;
- rate limits;
- known quirks.
