# Sdk

General-purpose clients for third-party APIs that lack a usable official .NET SDK. Every
project here could be copied into another product unchanged.

## Before adding a client

Check for an official, maintained .NET SDK. If one exists, use it directly from the module that
needs it. Write a client here only when there isn't one, or when the official one is unusable,
and record why in the client's README.

## Rules

- **Naming and dependencies.**
  - Name the project `Bagatka.Sdk.<Vendor>`.
  - It references only .NET, approved packages, and `Bagatka.Foundation`. Never `Bagatka.AiSloth.*`.
- **Vendor-shaped.**
  - Types and methods mirror the vendor's API and vocabulary.
  - Product concepts never appear here. The module that uses the client does the translation.
- **Minimal surface.** Cover only the endpoints a product actually uses. Add more when needed.
- **Registration.** Expose one client class (or a few, one per vendor API area), registered with
  one extension that takes the client's settings: `services.Add<Vendor>Client(settings)`, in a
  static class named `<Vendor>ClientRegistration`.
- **HTTP and resilience.**
  - For a remote service, use a typed `HttpClient` through `IHttpClientFactory`, with the standard
    resilience handler.
  - For a local socket, such as the Docker Engine's, the client may own one `HttpClient` and skip
    retries.
  - A client whose every call changes state at the vendor, such as OAuth token requests, retries
    nothing, so it may own one `HttpClient` with a pooled connection lifetime too
    (`Bagatka.Sdk.OpenAI`).
  - Nothing outside the client retries vendor calls.
  - Retry only idempotent requests, or requests carrying a vendor idempotency key.
- **Settings.** One immutable settings record that validates itself in its constructor, passed by
  the host (`PATTERNS.md`, entry 20). The client never reads `IConfiguration`, section names, or
  environment variables, so it works in any app and could be published to NuGet unchanged. Secrets
  come from settings, never from code.
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

- When the real service is free and local, such as the Docker Engine, test against it.
- Otherwise, test against recorded vendor responses, using a fake `HttpMessageHandler`. Live smoke
  tests are opt-in and never run in CI by default.

## Each client's README

Every client has a short README covering:

- a link to the vendor's docs and the API version used;
- the endpoints covered;
- the auth method;
- rate limits;
- known quirks.
