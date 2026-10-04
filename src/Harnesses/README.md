# Harnesses

A general-purpose library for running coding agents: which programs exist (harnesses), how to start
each and pay for its model, and the client's side of the Agent Client Protocol (ACP) they all
speak. Nothing here knows AiSloth; Chats is its caller.

## Contract

`Bagatka.Harnesses`:

- `HarnessProfiles` lists the known `HarnessProfile`s: Claude Code (through its ACP adapter) and GitHub
  Copilot CLI. A profile is data: the command, the `CredentialKind`s it takes, and the environment
  variables each goes in. A credential that `UsesGateway` never reaches the harness: it gets a model
  gateway's URL and token instead, and the gateway adds the secret.
- `Acp` builds the messages a client sends and reads what a harness writes into an `AcpEvent`: an
  update, a request to answer, or the answer to one of the client's requests, already matched to it.
  A prompt or steer carries the caller's key, and its answer comes back with it. The request IDs
  that carry keys are persisted wherever a host keeps harness output, so their format never changes.

```csharp
HarnessProfile harness = HarnessProfiles.Copilot;
IReadOnlyDictionary<string, string> environment = harness.EnvironmentFor(CredentialKind.GitHubToken, token, gateway: null);
// start harness.Command with harness.Arguments and environment; write Acp.Initialize() to it
AcpEvent? read = Acp.Read(line);   // null: noise, or an answer to nothing this client asked
switch (read?.Value)
{
    case AcpInitialized: Write(Acp.NewSession("/work")); break;
    case AcpSessionCreated created: Write(Acp.Prompt(messageId, created.SessionId, "Add a README")); break;
    case AcpUpdate update: ...; break;                       // save or show it
    case AcpRequest request: Write(Acp.Allow(request)); break;
    case AcpPromptEnded ended: ...; break;                   // ended.Prompt == messageId
    case AcpStartFailed failed: ...; break;                  // failed.Error: "Authentication required"
}
```

## Rules

- **A harness's programs are installed by the host** at the versions the profiles are written for:
  AiSloth builds one nook image per harness (`src/Daemon/Dockerfile`), so a host pulls only the
  harnesses its nooks carry.
- **Adding a harness** is a profile here and an install line in the host's image; a harness must
  speak ACP on standard input and output.
- **Secrets never reach a log.** Environments built here may hold them.

## Not built yet

- **Codex** (`codex-acp`): its profile comes with ChatGPT plan access for hosted apps, which OpenAI
  grants through an interest form.
- **Harness state:** each profile will list where its harness keeps memory, skills, and other files
  between sessions, so a host can save them per person and restore them into new places.
- **Resuming a session** (`session/load`), for forks.
