# Chats

A chat is a conversation with a coding agent working in one nook, run by the harness the nook
carries on an agent account that pays for its work. A chat is as open as its nook: whoever may read
the nook reads it, and whoever may write writes in it. A message reaches the agent when its sender
may use the chat's account; anyone else's is a proposal, which someone who may use it sends on, as is
or edited.
Each message shows who sent it. The agent works only inside its nook. Any harness that speaks
the Agent Client Protocol (ACP) can run a chat (`src/Harnesses`); today Claude Code and GitHub Copilot.

## Owns

- **Data:** chats, the messages and proposals people send, and every event of every chat.
- **Rules:** who may read and write (the nook's access levels), whose messages reach the agent and
  whose are proposals (see above), which harness and
  account a chat may run on, how a message reaches the agent (a new turn, steered into the running
  one, or cancelled by a stop), and what the agent may do without asking (anything inside its nook).
- **Runtime state:** each busy chat's runner, which talks to the agent, and the signals that wake
  watchers, in the memory of this instance.
- **Integrations:** harnesses, through `Bagatka.Harnesses`; what each kind of agent account is to a
  harness (`Harness/AccountCredentials.cs`) is the one place the two meet.

## Does not own

- Nooks and processes: Nooks. A chat's agent is an ordinary process in its nook.
- Agent accounts and their secrets: AgentAccounts. This module asks for a chat's secret when its
  agent starts, and when the model gateway forwards a call.
- Harness profiles and the protocol: `Bagatka.Harnesses`.
- What the agent can do in AiSloth itself: the public API, through MCP (not built yet).

## Contract

`IChatsApi` in `Bagatka.AiSloth.Chats.Contracts`: members list the harnesses, start chats in a nook
on its harness and an agent account, send messages (or send a proposal on), stop the agent, and watch
a chat's events from any sequence number.
`IChatHarnessesApi` is the model gateway's side, never a public route or a tool.

```csharp
Result<ChatSummary> started = await chats.StartAsync(alice, new StartChat(nookId, teamAccountId), ct); // the nook carries claude-code
if (started.Failed)
{
    return new Result(started.Error);
}

ChatSummary chat = started.Output;
Result<ChatMessage> sent = await chats.SendAsync(alice, new SendMessage(chat.Id, "Add a README"), ct); // joins the running turn or waits for the next
Result<IAsyncEnumerable<ChatEvent>> watch = await chats.WatchAsync(bob, new WatchChat(chat.Id, AfterSequence: 0), ct);
if (watch.Failed)
{
    return new Result(watch.Error);
}

await foreach (ChatEvent e in watch.Output)
{
    // MessageSent, TurnStarted, AgentUpdate (ACP session/update), ..., TurnEnded("end_turn");
    // MessageProposed when someone who may not use the account writes
}
```

## Asks

Workspaces (`GetAccessAsync`, on the chat's nook), on every call made for a user; Nooks, to check a
nook and to start, feed, watch, and stop the agent's process; AgentAccounts, for the account a chat
runs on and who may use it (`MayUseAsync`).

## Publishes

Nothing yet.

## Reacts to

Nothing yet. Once nooks publish `NookDeleted`, their chats go with them.

## Data

Schema `chats`. Tables `chats` (nook, workspace, who started it, the harness, the agent account and
its owner when personal; the agent's process, token hash,
session, and how far its output is read; the turn in progress; the last sequence number; `xmin`
as concurrency token), `messages` (text, sender, the proposal it sends on, and where each is on its
way to the agent, or that it is a proposal), and `events` (chat and sequence number as key, kind, and
the body as `jsonb`; an agent update is the ACP update as the agent sent it).

## Background work

- **Runners** (`Harness/ChatRunners.cs`, `Harness/ChatRunner.cs`): one per chat with work, started
  by a message or a stop, ending when the chat is idle. At startup, chats that had work get their
  runner back. A runner starts the agent with the first message, delivers messages, answers the
  agent's requests, and saves each batch of updates with the offset of the agent's output it has
  read, so a restart continues exactly where it stopped. It reaches the agent's process only through
  `Harness/AgentProcess.cs`, which turns the process into lines of text.

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
- **No API key in a nook.** For an Anthropic API key, the agent gets the model gateway's URL and a
  random token for this chat (only its hash is kept), and the gateway adds the account's key. A
  plan's token, such as Copilot's, goes to the harness itself: Copilot calls GitHub directly, and the
  token's only permission is Copilot requests.
- **Proposals instead of shared accounts.** Writing in a chat follows the nook; spending an account
  doesn't. Everyone who may write proposes, and only someone who may use the account sends to the
  agent: a workspace's account serves people with Write on the workspace, so a nook's guest proposes
  there, and a personal plan, such as a Claude subscription, is only ever used by its owner. Sending
  decides it once, by AgentAccounts' `MayUseAsync`, and the message keeps it. A removed account isn't
  anyone's to propose to: messages go through, and their turn fails with the reason.
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
- **Harness state per person.** A harness's memory, skills, and other files it keeps between
  sessions will be saved per person, from each chat's nook, and restored into their new nooks; it
  comes with object storage. Whose state a chat several people write in updates is decided then.
- **Codex,** once OpenAI grants plan access for hosted apps.
- **Deleted nooks.** A chat whose nook is gone stays; its next message fails with the reason.
- **Dismissing a proposal.** It simply stays.
- **Event volume.** Every streamed text chunk is a row; nothing merges them yet.
- **One active instance.** Runners and watch signals live in the instance's memory, as daemon
  connections do.
