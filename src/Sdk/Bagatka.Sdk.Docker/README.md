# Bagatka.Sdk.Docker

A minimal client for the Docker Engine API. Docker has no official .NET SDK, and the provider
needs only a dozen endpoints.

- **API:** [Docker Engine API v1.47](https://docs.docker.com/reference/api/engine/version/v1.47/),
  pinned in every request path. Docker 27 and later serve it.
- **Endpoints:** containers (create, inspect, list, start, stop, pause, unpause, remove), images (pull,
  inspect, list, remove), commit, and the engine's runtimes (from `/info`).
- **Transport and authentication:** the engine's Unix socket; access to the socket is the only
  authentication. Other transports are rejected by `DockerClientSettings`; on Windows, run inside
  WSL with Docker Desktop's WSL integration.
- **Rate limits:** none locally. Pulls from Docker Hub are rate-limited by Docker Hub.
- **No retries:** the engine is local, and the callers' own operations are safe to repeat.

## Quirks

- Inspect timestamps have up to nine fractional digits; the client trims them to the seven .NET
  parses.
- A commit adds every container environment variable missing from its own list, so callers that
  must keep values out of the image list those names with empty values.
- Removing an image by tag with force removes only the tag while containers use the image; the
  untagged image stays until it is removed by ID once unused.
- Image pulls report failures inside the response stream, not with the status code; the client
  reads the whole stream.

## Tests

Covered through the Docker sandbox provider's tests against a real engine
(`tests/Bagatka.Sandboxing.ConformanceTests`).
