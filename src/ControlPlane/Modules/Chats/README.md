# Chats

A chat is a conversation with one coding agent, working in a nook the chat creates for it, run by a
harness on an agent account that pays for its work. One chat, one nook, one agent: agents never work
on each other's files, and parallel work is more chats. A chat is as open as its nook. A message
reaches the agent when its sender may use the chat's account; anyone else's is a proposal, which
someone who may use it sends on. Any harness that speaks the Agent Client Protocol (ACP) can run a
chat (`src/Harnesses`).

## Owns

- **Data:** chats, their messages and proposals, every event of every chat, workspaces' and people's
  instructions, and drafts.
- **Rules:** who may read and write (the nook's access levels), whose messages reach the agent, which
  harness and account a chat may run on, how a message reaches the agent (a new turn, steered into
  the running one, or cancelled by a stop), that every turn ends with a checkpoint, when a new agent
  takes over a conversation, and which instructions and harness state an agent gets.
- **Runtime state:** each busy chat's runner and the signals that wake watchers, in the active
  instance's memory.

## Does not own

Nooks and processes (Nooks: a chat's agent is an ordinary process in its nook); agent accounts and
their secrets (AgentAccounts, asked when an agent starts and for each model call through the
gateway); harness profiles and ACP (`Bagatka.Harnesses`); where harness state is kept (Nooks' kept
folders).

## Contract

`IChatsApi`: members list the harnesses, start chats (each creating its nook, with the workspace's
repositories or another chat's files, now or at a checkpoint), list chats, send messages and send
proposals on, stop the agent, watch a chat's events from any sequence number, set and read the
instructions every agent gets, and list and forget their harness state. `IChatHarnessesApi` is the
model gateway's side.

```csharp
Result<ChatSummary> started = await chats.StartAsync(alice, new StartChat(workspaceId, "docker", "codex", accountId, [new NookRepository(apiRepositoryId)], CopyOf: null, Checkpoint: null), ct);
Result<ChatMessage> sent = await chats.SendAsync(alice, new SendMessage(started.Output.Id, "Add a README", Proposal: null), ct);
Result<IAsyncEnumerable<ChatEvent>> watch = await chats.WatchAsync(bob, new WatchChat(started.Output.Id, AfterSequence: 0), ct);
// SetupStarted, SetupEnded, MessageSent, TurnStarted, AgentUpdate (ACP's session/update), ...,
// CheckpointSaved, TurnEnded: a turn that ended has its files saved.
```

## Asks

Workspaces (access); Nooks (the chat's nook: create it, follow its setup, run, feed, and stop the
agent's process, take a checkpoint after each turn, copy instructions in, sync harness state);
AgentAccounts (the account a chat runs on, and who may use it).

## Data

Schema `chats`: `chats` (one per nook; the agent's process, session, and how far its output is read;
the turn in progress; `xmin` as concurrency token), `messages`, `events` (sequence numbers per chat;
an agent update is stored as the agent sent it), `drafts`, `workspace_instructions`, and
`personal_instructions`.

## Background work

- **Runners** (`Harness/ChatRunners.cs`, `Harness/ChatRunner.cs`): one per chat with work, on the
  active instance. A runner starts the agent once the nook's setup ended, keeps the nook awake while
  the chat has work, delivers messages, answers the agent's requests, and saves each batch of events
  with the offset of the agent's output it has read, so a restart continues where it stopped. A turn
  ends with a checkpoint of the nook's files, then `TurnEnded`, then a harness state sync.
- **Drafts** (`Drafts.cs`): every 10 seconds, deletes drafts older than the draft lifetime with their
  nooks.

## Configuration

`ChatsSettings`: the model gateway's URL as an agent in a nook reaches it, and the draft lifetime (30
minutes unless given).

## Decisions and constraints

- **A chat is a draft until its first message.** Apps start a chat as someone starts writing, so its
  nook and agent are ready when they send. A draft isn't listed, and goes with its nook after the
  draft lifetime; its nook sleeps after two minutes like any other.
- **ACP over the process primitive.** The agent is a nook process with `Complete` output retention;
  its standard input and output carry ACP, and this module is the client. A deploy pauses a chat for
  seconds; the agent keeps working.
- **The agent acts without asking inside its nook,** as `system:chats.harness`: its permission
  requests get their broadest allow.
- **No model key or plan in a nook.** An agent gets the model gateway's URL and a token for its chat;
  the gateway forwards each call with the headers that pay for it. A plan's token tied to one harness,
  such as Copilot's, goes to the harness itself.
- **Every turn ends with a checkpoint,** however it ended, before `TurnEnded`, so clients wait for one
  event. A failed checkpoint is told (`CheckpointFailed`), and the chat goes on. A nearly full disk is
  told too (`DiskNearlyFull`), since the next checkpoints may fail.
- **A new agent continues the conversation.** Checkpoints keep the harness's sessions, and an agent
  that takes over loads the earlier session when its harness can (`AgentRestarted`). An agent lost
  with its nook hands its turn to the next one.
- **Instructions are AiSloth's, not a harness's.** AiSloth's own (the nook, its repositories, how
  setups work), the workspace's, and the person's are written before each agent starts to the file
  its harness reads its user's instructions from, outside `/work`. Asking an agent to prepare a chat
  is an ordinary message: these instructions say how.
- **Harness state follows who may direct the agents.** What a harness writes for later
  (`HarnessProfile.StatePaths`, such as Claude Code's memory) is a kept folder per workspace and
  harness, for a person's chats on their own accounts or for the workspace's chats on its accounts,
  synced when the agent starts and after each turn, so parallel chats share what each learns, by
  lines. Nobody's notes reach agents they couldn't direct themselves.
- **A chat on a personal account reserves its nook for the account's owner,** as whoever runs
  something in a nook can use what its agent can, the account's token among it. Others watch and
  propose.
- **How long people wait is measured:** `aisloth.chats.first_action`, from a message sent to the
  agent's first action for it.
