# Harnesses

A general-purpose library for running coding agents: which programs exist (harnesses), how to start
each and pay for its model, the client's side of the Agent Client Protocol (ACP) they all speak, and
an agent's session over it. Nothing here knows AiSloth; Chats is its caller.

## Contract

`Bagatka.Harnesses`:

- `HarnessProfiles` lists the known `HarnessProfile`s: Claude Code, Codex, and pi (each through its ACP
  adapter), and GitHub Copilot CLI. A profile is data: an ID, a name, the `CredentialKind`s it takes,
  where it keeps its sessions (`SessionPaths`), which a host saves with each nook so a new agent can
  load one, the file it reads its user's standing instructions from (`InstructionsPath`), and where
  it keeps what it writes for itself to use later (`StatePaths`: Claude Code's memory, which its start
  script puts in `~/.claude/memory` whatever folder it works in; never credentials, nor what its start
  script writes), the models it's made for (`ModelFamily`), and how hard it can think, in its own
  words, and unless told otherwise (`ThinkingLevels`, `DefaultThinking`: Claude Code's efforts low to
  max, Codex's reasoning low to xhigh, pi's thinking off to max, Copilot's none to max).
- **Every harness starts the same way.** Its image provides `harness` (`HarnessProfile.Command`), its
  start script from `start/<id>.sh`, and the host starts it with the same variables
  (`HarnessProfile.EnvironmentFor`):

  | Variable | Value |
  |---|---|
  | `HARNESS_CREDENTIAL` | `openai`, `codex` (ChatGPT's Codex backend), `anthropic`, `claude-plan` (Anthropic's API on a Claude plan), `openrouter`, or `copilot` |
  | `HARNESS_MODEL_URL` | the model gateway's URL |
  | `HARNESS_TOKEN` | the gateway's token for the chat |
  | `HARNESS_MODEL` | the model's ID as its vendor names it (`IsModelId`) |
  | `HARNESS_THINKING` | one of its `ThinkingLevels` |

  The script translates them into its harness's own configuration (environment variables, a file,
  or its adapter's gateway sign-in) and runs it. Every model API goes through a model gateway, so
  the key or plan behind it never reaches the harness. A Claude plan signs Claude Code in to the
  gateway as to Anthropic, with an OAuth token that is the chat's; the gateway calls Anthropic with
  the plan's own.
  OpenRouter is one credential for two APIs: Claude Code calls its Anthropic API at the gateway's
  root, with OpenRouter's newest Claude models behind its aliases, and pi its OpenAI chat API at
  `/v1`. Copilot's API is one credential for both of OpenAI's: Copilot's CLI and pi take it as an
  OpenAI-style provider at the gateway, each in the API Copilot says the chat's model speaks, which
  the images' `copilot-model` (`start/copilot-model.sh`) reads from Copilot's list of models.
- `AgentSession` is one agent's conversation over ACP as plain state its caller keeps with whatever it
  saves, so another process can continue it: the session prompts go to, the earlier session the next
  agent continues when it can load it, whether the agent takes messages into a running turn, and
  whether it replays history. `Read` takes each line the harness writes and answers with the next
  state, the lines to write back, and what happened (`AgentEvent`): progress (`AgentReported`), the
  session ready, remembering the conversation or not (`SessionReady`), a prompt or a steer answered
  (`PromptAnswered`, `SteerAnswered`), or an agent that can't start (`StartFailed`). It answers the
  harness's requests itself, loads the earlier session when the agent can, and skips the history a
  loaded session replays. `Prompt`, `Steer`, and `Cancel` give the lines that send a message or stop
  the turn. A prompt or steer carries the caller's key, and its answer comes back with it; the request
  IDs that carry keys are persisted wherever a host keeps harness output, so their format never
  changes. It does no I/O.

```csharp
IReadOnlyDictionary<string, string> environment = harness.EnvironmentFor(CredentialKind.OpenAIApi, gatewayToken, gateway, "gpt-6.1-sol", "medium");
// start HarnessProfile.Command with environment in the harness's image, then write:
Write(AgentSession.Start("aisloth", "AiSloth", "1.0"));
AgentSession session = saved.Ended();                       // the last agent's session, which this one continues when it can

AgentStep step = session.Read(line, "/work");               // each line the harness writes
Write(step.Lines);                                          // session/new or session/load, answers to its requests
session = step.Session;                                     // save it with what happened
switch (step.Event?.Value)
{
    case SessionReady ready: Write(session.Prompt(messageKey, "Add a README")); break;
    case AgentReported reported: ...; break;                // save or show it
    case PromptAnswered answered: ...; break;               // answered.Prompt == messageKey
    case StartFailed failed: ...; break;                    // failed.Error: "Authentication required"
}
```

## Rules

- **A harness's programs are installed by the host** at the versions its start script is written
  for: AiSloth builds one nook image per harness (`src/Daemon/Dockerfile`), with the script as
  `harness`, so a host pulls only the harnesses its nooks carry.
- **Adding a harness** is a profile here, a start script in `start/`, and an install line in the
  host's image; a harness must speak ACP on standard input and output.
- **Secrets never reach a log.** Environments built here hold them.
