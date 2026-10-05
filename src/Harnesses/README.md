# Harnesses

A general-purpose library for running coding agents: which programs exist (harnesses), how to start
each and pay for its model, and the client's side of the Agent Client Protocol (ACP) they all
speak. Nothing here knows AiSloth; Chats is its caller.

## Contract

`Bagatka.Harnesses`:

- `HarnessProfiles` lists the known `HarnessProfile`s: Claude Code, Codex, and pi (each through its ACP
  adapter), and GitHub Copilot CLI. A profile is data: an ID, a name, the `CredentialKind`s it takes,
  where it keeps its sessions (`SessionPaths`), which a host saves with each nook so a new agent can
  load one, the file it reads its user's standing instructions from (`InstructionsPath`), and where
  it keeps what it writes for itself to use later (`StatePaths`: Claude Code's memory, which its start
  script puts in `~/.claude/memory` whatever folder it works in; never credentials, nor what its start
  script writes).
- **Every harness starts the same way.** Its image provides `harness` (`HarnessProfile.Command`), its
  start script from `start/<id>.sh`, and the host starts it with the same three variables
  (`HarnessProfile.EnvironmentFor`):

  | Variable | Value |
  |---|---|
  | `HARNESS_CREDENTIAL` | `openai`, `anthropic`, `github-token`, or `claude-oauth-token` |
  | `HARNESS_MODEL_URL` | the model gateway's URL, for `openai` and `anthropic` |
  | `HARNESS_TOKEN` | the gateway's token, or the harness's own token |

  The script translates them into its harness's own configuration (environment variables, a file,
  or its adapter's gateway sign-in) and runs it. A model API always goes through a model gateway, so
  the key or plan behind it never reaches the harness; only a token tied to one harness goes to it.
- `Acp` builds the messages a client sends and reads what a harness writes into an `AcpEvent`: an
  update, a request to answer, or the answer to one of the client's requests, already matched to it.
  A prompt or steer carries the caller's key, and its answer comes back with it. The request IDs
  that carry keys are persisted wherever a host keeps harness output, so their format never changes.
  `Acp.LoadSession` continues an earlier session whose files the harness kept, when it says it can
  (`AcpInitialized.SupportsLoading`); the history it replays comes as updates before the answer.

```csharp
IReadOnlyDictionary<string, string> environment = HarnessProfile.EnvironmentFor(CredentialKind.OpenAIApi, gatewayToken, gateway);
// start HarnessProfile.Command with environment in the harness's image; write Acp.Initialize("aisloth", "AiSloth", "1.0") to it
AcpEvent? read = Acp.Read(line);   // null: noise, or an answer to nothing this client asked
switch (read?.Value)
{
    case AcpInitialized initialized: Write(initialized.SupportsLoading && earlier is not null ? Acp.LoadSession(earlier, "/work") : Acp.NewSession("/work")); break;
    case AcpSessionLoaded: ...; break;                       // continues the conversation; AcpLoadFailed: start a new one
    case AcpSessionCreated created: Write(Acp.Prompt(messageId, created.SessionId, "Add a README")); break;
    case AcpUpdate update: ...; break;                       // save or show it
    case AcpRequest request: Write(Acp.Allow(request)); break;
    case AcpPromptEnded ended: ...; break;                   // ended.Prompt == messageId
    case AcpStartFailed failed: ...; break;                  // failed.Error: "Authentication required"
}
```

## Rules

- **A harness's programs are installed by the host** at the versions its start script is written
  for: AiSloth builds one nook image per harness (`src/Daemon/Dockerfile`), with the script as
  `harness`, so a host pulls only the harnesses its nooks carry.
- **Adding a harness** is a profile here, a start script in `start/`, and an install line in the
  host's image; a harness must speak ACP on standard input and output.
- **Secrets never reach a log.** Environments built here hold them.

## Not built yet

- **State for Codex, pi, and Copilot.** None of them writes a memory of its own that we know of, so
  their profiles keep none. Copilot's instructions file is confirmed by `copilot instruction list`,
  not by a test, since no fake serves Copilot.
- **Forking a session** (`session/fork`), for continuing a conversation from an earlier turn.
