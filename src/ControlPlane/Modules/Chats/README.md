# Chats

A chat is a conversation with a coding agent working in one nook. Everyone in the nook's workspace
can read it and message the agent, and each message shows who sent it. The agent works only inside
its nook. Any harness that speaks the Agent Client Protocol (ACP) can run a chat; today that is
Claude Code, through its ACP adapter.

## Owns

- **Data:** chats, the messages people send, and every event of every chat.
- **Rules:** who may take part (members of the nook's workspace), how a message reaches the agent
  (a new turn, steered into the running one, or cancelled by a stop), and what the agent may do
  without asking (anything inside its nook).
- **Runtime state:** each busy chat's runner, which talks to the agent, and the signals that wake
  watchers, in the memory of this instance.
- **Integrations:** the harness: which program runs and what it needs (`Harness/ClaudeCodeHarness.cs`).

## Does not own

- Nooks and processes: Nooks. A chat's agent is an ordinary process in its nook.
- The model provider and its key: the WebApi's model gateway forwards agents' calls, and asks this
  module whether a call's token belongs to a chat.
- What the agent can do in AiSloth itself: the public API, through MCP (not built yet).

## Contract

`IChatsApi` in `Bagatka.AiSloth.Chats.Contracts`: members start chats in a nook, send messages,
stop the agent, and watch a chat's events from any sequence number. `IChatHarnessesApi` is the
model gateway's side, never a public route or a tool.

```csharp
ChatSummary chat = (await chats.StartAsync(alice, new StartChat(nookId), ct)).Value;
await chats.SendAsync(alice, new SendMessage(chat.Id, "Add a README"), ct);
await foreach (ChatEvent e in (await chats.WatchAsync(bob, new WatchChat(chat.Id, AfterSequence: 0), ct)).Value)
{
    // MessageSent, TurnStarted, AgentUpdate (ACP session/update), ..., TurnEnded("end_turn")
}
```

## Asks

Workspaces (`GetRoleAsync`), on every call made for a user; Nooks, to check a nook and to start,
feed, watch, and stop the agent's process.

## Publishes

Nothing yet.

## Reacts to

Nothing yet. Once nooks publish `NookDeleted`, their chats go with them.

## Data

Schema `chats`. Tables `chats` (nook, workspace, who started it; the agent's process, token hash,
session, and how far its output is read; the turn in progress; the last sequence number; `xmin`
as concurrency token), `messages` (text, sender, and where each is on its way to the agent), and
`events` (chat and sequence number as key, kind, and the body as `jsonb`; an agent update is the
ACP update as the agent sent it).

## Background work

- **Runners** (`Harness/ChatRunners.cs`, `Harness/ChatRunner.cs`): one per chat with work, started
  by a message or a stop, ending when the chat is idle. At startup, chats that had work get their
  runner back. A runner starts the agent with the first message, delivers messages, answers the
  agent's requests, and saves each batch of updates with the offset of the agent's output it has
  read, so a restart continues exactly where it stopped.

## Configuration

`ChatsSettings`, passed by the host (`PATTERNS.md`, entry 20): the connection string and the model
gateway's URL as an agent in a nook reaches it.

## Decisions and constraints

- **ACP over the process primitive.** The agent is a nook process with `Complete` output retention;
  its standard input and output carry ACP, one JSON-RPC message per line, and this module is the
  client. The daemon never knows about agents, and a control-plane deploy only pauses a chat for
  seconds: the agent keeps working, and its output waits in the nook.
- **The agent runs as `system:chats.harness`.** Nooks lets the control plane's own processes use
  nooks; a person's permissions never switch with whoever wrote last.
- **A message is never refused.** With no turn running, it starts one. During a turn, it is steered
  into it when the agent supports steering (`_session/steering`, which Claude's and Codex's adapters
  offer), and otherwise waits and starts the next turn. Stopping ends the turn at once as `cancelled`
  and cancels messages the agent hasn't received; the agent's process keeps running.
- **The agent acts without asking inside its nook.** Its permission requests get their broadest
  allow: the nook is isolated, and asking would stop unattended runs. Sensitive AiSloth operations
  will still need a person's confirmation once agents reach the public API.
- **No model key in a nook.** The agent gets the model gateway's URL and a random token for this
  chat (only its hash is kept). The gateway adds the deployment's key.
- **Events in ACP's own shape.** An agent update is stored and served unchanged, so a new harness
  or update kind needs no code here. The cost: ACP v1's shape is part of the stored data and the API.
- **Every event is saved here.** A chat's history outlives its agent and its nook's suspension.

## Not built yet

- **Checkpoints and forks.** Nothing is saved from the nook after a turn yet, and a chat can't be
  forked; both come next, with object storage. Until then, losing a nook loses its files.
- **Resuming a conversation in a new agent.** When the agent's process ends, the next message starts
  a new session without the earlier context (`session/load` comes with forks).
- **A nearly full disk asks for confirmation** before a new message; it comes with checkpoints.
- **MCP.** Agents can't use AiSloth's public API yet.
- **Harness profiles and harness state** (memory and skills across nooks), and harnesses other than
  Claude Code.
- **Deleted nooks.** A chat whose nook is gone stays; its next message fails with the reason.
- **Event volume.** Every streamed text chunk is a row; nothing merges them yet.
- **One active instance.** Runners and watch signals live in the instance's memory, as daemon
  connections do.
