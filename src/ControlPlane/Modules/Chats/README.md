# Chats

A chat is a conversation with one coding agent, working in a nook the chat creates for it, run by a
harness on an agent account that pays for its work. One chat, one nook, one agent: agents never work
on each other's files, and parallel work is more chats. A chat is as open as its nook: whoever may read
the nook reads it, and whoever may write writes in it. A message reaches the agent when its sender
may use the chat's account; anyone else's is a proposal, which someone who may use it sends on, as is
or edited.
Each message shows who sent it. The agent works only inside its nook. Any harness that speaks
the Agent Client Protocol (ACP) can run a chat (`src/Harnesses`); today Claude Code, Codex, pi, and
GitHub Copilot.

## Owns

- **Data:** chats, the messages and proposals people send, every event of every chat, workspaces'
  and people's instructions, and each person's harness state in each workspace, whose archives are
  in object storage.
- **Rules:** who may read and write (the nook's access levels), whose messages reach the agent and
  whose are proposals (see above), which harness and
  account a chat may run on, how a message reaches the agent (a new turn, steered into the running
  one, or cancelled by a stop), what the agent may do without asking (anything inside its nook),
  that every turn ends with a checkpoint, when a new agent takes over a conversation, which
  instructions and harness state an agent gets, and when a nearly full disk needs the sender's
  confirmation.
- **Runtime state:** each busy chat's runner, which talks to the agent, and the signals that wake
  watchers, in the memory of this instance.
- **Integrations:** harnesses, through `Bagatka.Harnesses`; what each kind of agent account is to a
  harness (`Harness/AccountCredentials.cs`) is the one place the two meet.

## Does not own

- Nooks and processes: Nooks. A chat's agent is an ordinary process in its nook.
- Agent accounts and their secrets: AgentAccounts. This module asks what a chat's account takes when
  its agent starts, and where to forward each call through the model gateway
  (`GetModelEndpointAsync`).
- Harness profiles and the protocol: `Bagatka.Harnesses`.
- What the agent can do in AiSloth itself: the public API, through MCP (not built yet).

## Contract

`IChatsApi` in `Bagatka.AiSloth.Chats.Contracts`: members list the harnesses, start chats (each
creating its nook, on a provider, with a harness and an agent account, and with the workspace's
repositories or another chat's files, as they are or at one of its checkpoints, and starting its
agent after its nook's setup), prepare a chat (the agent writes a setup for its files, and a fresh
nook tests it), list a workspace's
chats, send messages (or send a proposal on), stop the agent, watch a chat's events from any
sequence number, set and read the instructions every agent gets, and list and forget a person's
harness state in a workspace.
`IChatHarnessesApi` is the model gateway's side, never a public route or a tool.

```csharp
Result<ChatSummary> started = await chats.StartAsync(alice, new StartChat(workspaceId, "docker", "codex", teamAccountId, [new NookRepository(apiRepositoryId)], CopyOf: null, Checkpoint: null), ct); // creates its nook
if (started.Failed)
{
    return new Result(started.Error);
}

ChatSummary chat = started.Output;
Result<ChatMessage> sent = await chats.SendAsync(alice, new SendMessage(chat.Id, "Add a README", Proposal: null, ConfirmNearlyFullDisk: false), ct); // joins the running turn or waits for the next
Result<IAsyncEnumerable<ChatEvent>> watch = await chats.WatchAsync(bob, new WatchChat(chat.Id, AfterSequence: 0), ct);
if (watch.Failed)
{
    return new Result(watch.Error);
}

await foreach (ChatEvent e in watch.Output)
{
    // SetupStarted([".agents/setup"]), SetupEnded(0, ...), MessageSent, TurnStarted, AgentUpdate
    // (ACP session/update), ..., TurnEnded("end_turn"), CheckpointSaved(1); MessageProposed when
    // someone who may not use the account writes; AgentRestarted(Remembers: true) when a new agent
    // took over the conversation
}
```

## Asks

Workspaces (`GetAccessAsync`, on the workspace when a chat starts and on the chat's nook after), on
every call made for a user; Nooks, to create a chat's nook, to follow its setup, to test a setup in a
fresh nook it creates, runs twice, and deletes, as the person who asked, to start, feed, watch, and
stop the agent's process, to take a checkpoint after each turn, to copy instructions in and harness state
out and in, and for its disk usage; AgentAccounts, for the account a chat runs on and who may use it (`MayUseAsync`).

## Publishes

Nothing yet.

## Reacts to

Nothing yet. Once nooks publish `NookDeleted`, their chats go with them.

## Data

Schema `chats`. Tables `chats` (nook, unique: one chat per nook; workspace, who started it, the harness, the agent account and
its owner when personal; whether its agent still starts with it; the nook's setup run it told and
how that ended; the message whose turn a setup test follows, until it ended; the agent's process, token hash,
session, and how far its output is read; the session a new agent loads, and whether it is loading;
the turn in progress; the turn a checkpoint is due after; the file list of the harness state its
nook held at its last sync; the last sequence number; `xmin` as concurrency token), `messages` (text, sender, the
proposal it sends on, where each is on its way to the agent, or that it is a proposal, and which
setup test its turn's end calls for), `events`
(chat and sequence number as key, kind, and the body as `jsonb`; an agent update is the ACP update
as the agent sent it), `workspace_instructions` and `personal_instructions` (the text, when, and for
a workspace's who last changed it), `drafts` (a chat nobody wrote in yet: its nook, workspace, who
started it and when), and `harness_states` (person, workspace, and harness as key, when
and from which chat it was saved, its size, its version: the SHA-256 of its file list; `xmin` as
concurrency token). Archives are in object storage under
`people/<user ID>/workspaces/<workspace ID>/harness-state/<harness>/<version>/`.

## Background work

- **Runners** (`Harness/ChatRunners.cs`, `Harness/ChatRunner.cs`): one per chat with work, started
  by a new chat, a message, or a stop, ending when the chat is idle. At startup, chats that had work
  get their runner back. A runner starts the agent once the nook's setup ended, following the setup
  run meanwhile, keeps the nook awake while the chat has work, tests a setup the agent prepared
  (`Harness/NookSetups.cs`), delivers messages, answers the
  agent's requests, and saves each batch of updates with the offset of the agent's output it has
  read, so a restart continues exactly where it stopped. It reaches the agent's process only through
  `Harness/AgentProcess.cs`, which turns the process into lines of text. After each turn it takes
  the nook's checkpoint and syncs the harness state (`Harness/HarnessStates.cs`) before the next
  turn starts. A runner whose chat is gone retires.
- **Drafts** (`Drafts.cs`): every 10 seconds, deletes drafts older than the draft lifetime, with
  their nooks.

## Configuration

`ChatsSettings`, passed by the host (`PATTERNS.md`, entry 20): the connection string, the model
gateway's URL as an agent in a nook reaches it, how full a nook's disk is when a message needs
confirming (0.9 by default), and the draft lifetime (15 minutes by default). The host also registers the object storage harness state is kept in.

## Decisions and constraints

- **A chat is a draft until its first message.** Apps start a chat as someone starts writing, so its
  nook and agent are ready when they send. A draft isn't listed, goes with its nook after the draft
  lifetime, and a person keeps at most two in a workspace, the oldest going when they start another.
  The first message ends the draft in the same save, so a draft is never deleted with a message in it.

- **One chat, one nook, one agent.** Starting a chat creates its nook, so no two agents ever work on
  the same files. Nooks may still exist without a chat, for processes only, but a chat never joins a
  nook that exists. Should saving a chat fail after its nook was created, the nook stays, without an
  agent, until someone deletes it.
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
- **No key or plan of a model API in a nook.** For an API key or a ChatGPT plan, the agent gets the
  model gateway's URL and a random token for this chat (only its hash is kept), and the gateway
  forwards each call to the account's endpoint with the headers that pay for it. Every harness starts
  the same way, with the gateway's URL and the token in the same variables (`src/Harnesses`). A plan's
  token tied to one harness, such as Copilot's, goes to the harness itself: Copilot calls GitHub
  directly, and the token's only permission is Copilot requests.
- **Agents name this client** `aisloth` when they initialize, which Codex passes on to OpenAI.
- **Proposals instead of shared accounts.** Writing in a chat follows the nook; spending an account
  doesn't. Everyone who may write proposes, and only someone who may use the account sends to the
  agent: a workspace's account serves people with Write on the workspace, so a nook's guest proposes
  there, and a personal plan, such as a Claude subscription, is only ever used by its owner. Sending
  decides it once, by AgentAccounts' `MayUseAsync`, and the message keeps it. A removed account isn't
  anyone's to propose to: messages go through, and their turn fails with the reason.
- **Events in ACP's own shape.** An agent update is stored and served unchanged, so a new harness
  or update kind needs no code here. The cost: ACP v1's shape is part of the stored data and the API.
- **Agents start with their chat, after the nook's setup.** Creating a chat starts its agent, so
  it is ready while people write. It starts once the nook's setup ended (Nooks, "Setup"), which the
  chat tells (`SetupStarted`, `SetupEnded`); messages sent meanwhile wait, and the chat keeps taking
  them. A failed setup shows the end of its output, and the agent starts anyway, told in AiSloth's
  instructions how it failed and where its whole output is, so it can fix the cause, or tell people
  what they must do. A nook that gets its files again, after its sandbox was lost, runs its setup
  again, and the next agent waits for that run.
- **Preparing is the agent's work, proven by AiSloth.** Preparing a chat sends the agent AiSloth's
  request, as a message from the person, to write a setup for the chat's files, run it, and commit it. After
  that turn's checkpoint, a fresh nook on the chat's provider, with its harness's image and the
  checkpoint's files, runs the setup from scratch, then again, as a nook from a ready copy does,
  leaving a fresh ready copy when it took a while; it is deleted
  afterwards, and the next turn waits meanwhile. A failed test goes back to the agent as a message
  with the end of its output, and its turn is tested again, three tests at most. Only someone who
  may use the chat's account prepares, because the agent and the tests spend that person's account
  and compute. Stopping the agent stops the test.
- **A chat with work keeps its nook awake.** While its agent's turn runs, messages wait, or its
  agent starts, the chat's runner wakes its nook for 30 seconds every 10, however long the agent
  thinks without a word; once idle, the nook falls asleep after its sleep period. A nook that
  slept without its memory ended its agent, so the next message starts a new one, which loads the
  conversation (`AgentRestarted`), after the nook's resume scripts.
- **How long people wait is measured.** The meter `Bagatka.AiSloth.Chats` records
  `aisloth.chats.first_action`: seconds from a message sent to the first thing its agent does for it
  (thinking, answering, using a tool, or planning), with any nook, setup, and agent start before it,
  by harness.
- **Every event is saved here.** A chat's history outlives its agent and its nook's suspension.
- **Every turn ends with a checkpoint,** however it ended, before the next turn starts, so the
  files a turn changed can be downloaded, started from (`StartChat.Checkpoint`), and come back with
  a lost nook. A failed checkpoint is told (`CheckpointFailed`), and the chat goes on.
- **A new agent continues the conversation.** Checkpoints keep the harness's sessions (its profile's
  session paths), and an agent that took over after another ended loads the earlier session
  (`session/load`) when its harness can, ignoring the history it replays; otherwise it starts a new
  one. Either way the chat says so (`AgentRestarted`). An agent lost with its nook (exit code -1)
  hands its turn to the next one, which gets the turn's message again in the nook's latest
  checkpoint; any other exit ends the turn as failed, so a message that crashes its agent isn't
  retried.
- **Instructions are AiSloth's, not a harness's.** A workspace's and the chat starter's own
  instructions are written, before each agent starts, to the file its harness reads its user's
  standing instructions from (`HarnessProfile.InstructionsPath`), outside `/work`, so they never meet
  a repository's files and every harness follows the same text. What belongs to one repository stays
  in that repository's `AGENTS.md` or `CLAUDE.md`. An agent that can't get its instructions doesn't
  start. Changes reach agents that start afterwards; a running agent keeps the ones it started with.
- **Harness state follows the person who started the chat, within its workspace, kept in step
  across their chats there.** What a harness writes for itself to use later (its profile's state
  paths: today Claude Code's memory) syncs at a turn's edges, while the agent doesn't write: when the
  agent starts, after each turn, and before a turn when another chat saved since. A sync merges what
  the nook changed since its last one with what others saved meanwhile, file by file
  (`Harness/StateFiles.cs`): a file one side changed, added, or deleted takes that side's, and a file
  both changed keeps both sides' lines, so nothing learned is lost, at worst a line twice. The result
  is saved as a new version, guarded by the row's concurrency token (a chat that loses the race
  merges again), and put back in the nook. It never leaves its workspace, because it may describe
  that workspace's work. Nothing reaches a running turn: the agent would race it, and Claude Code
  reads its memory index when a session starts or compacts, so a turn sees others' changes in the
  files it opens and its next agent sees them all. Failures are logged; the agent works with what its
  nook has, and the next sync tries again.
- **A nearly full disk asks first.** A message for the agent while the nook's disk is at the
  threshold or above is refused (`DiskNearlyFull`) until the sender confirms, because the agent's
  writes and the checkpoints may fail. Proposals don't run the agent, so they never ask.

## Not built yet

- **Asking people for what setup needs.** An agent that needs a secret tells people the command
  (`sloth secret set`); the apps will show a button where someone allowed to can paste it.
- **Forks.** A chat starts from another's files at a checkpoint, but not from its conversation.
- **MCP.** Agents can't use AiSloth's public API yet.
- **Codex,** once OpenAI grants plan access for hosted apps.
- **Deleted nooks.** A chat whose nook is gone stays; its next message fails with the reason.
- **Dismissing a proposal.** It simply stays.
- **Harness state edges.** An agent that deletes all of its state gets it back from the saved one,
  as a nook created again after it was lost does; a binary file both sides changed keeps this chat's;
  a state over 1,000 files or 16 MiB isn't kept; and a save that loses a race leaves its archive
  behind in object storage.
- **Event volume.** Every streamed text chunk is a row; nothing merges them yet.
- **One active instance.** Runners and watch signals live in the instance's memory, as daemon
  connections do.
