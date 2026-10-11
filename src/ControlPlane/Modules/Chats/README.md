# Chats

A chat is a conversation with one coding agent, working in a nook the chat creates for it, run by a
harness on an agent account that pays for its work. One chat, one nook, one agent: agents never work
on each other's files, and parallel work is more chats. A chat is as open as its nook. A message
reaches the agent when its sender may use the chat's account; anyone else's is a proposal, which
someone who may use it sends on. Any harness that speaks the Agent Client Protocol (ACP) can run a
chat (`src/Harnesses`).

## Owns

- **Data:** chats, their messages and proposals, every event of every chat, workspaces' and people's
  instructions, drafts, and people's presets.
- **Rules:** who may read and write (the nook's access levels), whose messages reach the agent, which
  harness, account, and model a chat may run on, how a message reaches the agent (a new turn, steered into
  the running one, or cancelled by a stop), that every turn ends with a checkpoint, when a new agent
  takes over a conversation, and which instructions and harness state an agent gets.
- **Runtime state:** each busy chat's runner and the signals that wake watchers of a chat or of a
  workspace's list, in the active instance's memory.

## Does not own

Nooks and processes (Nooks: a chat's agent is an ordinary process in its nook); agent accounts and
their secrets (AgentAccounts, asked when an agent starts and for each model call through the
gateway); harness profiles and ACP (`Bagatka.Harnesses`); where harness state is kept (Nooks' kept
folders).

## Contract

`IChatsApi`: members list the harnesses and the models each can run on with an account, start chats
(each creating its nook, with the workspace's repositories or another chat's files, now or at a
checkpoint, and that chat's conversation when it continues it), list chats or watch the list as it changes, send
messages and send proposals on, change the model and account a chat runs on, stop the agent, run a
chat's setup again, start a chat that fixes it and bring its fix in, watch a chat's events from any sequence number, set
and read the instructions every agent gets, list and forget their harness state, and keep presets. `IChatHarnessesApi` is the
model gateway's side.

```csharp
Result<ChatSummary> started = await chats.StartAsync(alice, new StartChat(workspaceId, "docker", "pi", openRouterKeyId, "openai/gpt-6-luna", "medium", [new NookRepository(apiRepositoryId)], CopyOf: null, Checkpoint: null, WithConversation: false), ct);
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

Schema `chats`: `chats` (one per nook; its harness, account, and model, and another account and
model chosen while an agent ran; the chat it continues, up to which event; whether its setup is to run again; the agent's process, session,
and how far its output is read; the turn in progress; `xmin` as concurrency token), `messages`,
`events` (sequence numbers per chat; an agent update is stored as the agent sent it), `drafts`,
`workspace_instructions`, `personal_instructions`, `presets` (a person's harness, account, and
model, each once), and `model_calls` (each call the gateway passed: its chat, nook, workspace,
account, and turn, its tokens, its cost then, and until when its cache stays).

## Background work

- **Runners** (`Harness/ChatRunners.cs`, `Harness/ChatRunner.cs`): one per chat with work, on the
  active instance. A runner starts the agent once the nook's setup ended, runs the setup again when
  asked once the agent answered everything and stopped, then starts it again, keeps the nook awake while
  the chat has work, delivers messages, answers the agent's requests, and saves each batch of events
  with the offset of the agent's output it has read, so a restart continues where it stopped. A turn
  ends with a checkpoint of the nook's files, then `TurnEnded`, then a harness state sync.
- **Drafts** (`Drafts.cs`): every 10 seconds, deletes drafts older than the draft lifetime with their
  nooks.

## Configuration

`ChatsSettings`: the model gateway's URL as an agent in a nook reaches it, and the draft lifetime (30
minutes unless given).

## Decisions and constraints

- **A continuing chat gets the conversation as a file, never a summary.** It starts in a copy of the
  other chat's files with its conversation up to then as plain Markdown
  (`/work/.aisloth/chats/<id>.md`, `ConversationMarkdown`): what people wrote, what the agent
  answered, and a line for each thing it did, without its thinking or tools' output. The instructions
  name the file; the agent starts a session of its own, with an empty context, and reads it when it
  needs it. Being in `/work`, the file goes with later copies, so a chain of them keeps every
  conversation.

- **A chat is a draft until its first message.** Apps start a chat as someone starts writing, so its
  nook and agent are ready when they send. A draft isn't listed, and goes with its nook after the
  draft lifetime; its nook sleeps after two minutes like any other.
- **ACP over the process primitive.** The agent is a nook process with `Complete` output retention;
  its standard input and output carry ACP, and this module is the client. A deploy pauses a chat for
  seconds; the agent keeps working.
- **The agent acts without asking inside its nook,** as `system:chats.harness`: its permission
  requests get their broadest allow.
- **Every chat names its model and thinking level.** Nothing is left to a harness's own default, so
  what runs is what the chat shows. A model is the vendor's ID, such as `openai/gpt-6-luna` at
  OpenRouter. The models offered are the vendor's that can call tools, those of the harness's own
  family first (`HarnessProfile.ModelFamily`); a chat's model isn't checked against them, so a model
  the vendor doesn't know fails its turns with the vendor's answer. A chat older than this rule has
  no model and takes one before its next message (`ChatsErrors.ModelNotChosen`).
- **A chat's model changes between turns.** Its harness stays, since its nook runs it, but whoever
  may use the chat's account picks another model, thinking level, or account of the same owner's,
  for the next turn (`ModelChanged`). Thinking levels are each harness's own words. A running agent finishes its turn on its own account, so its calls never
  reach another vendor under the old model's name; then it stops, and the next agent starts on the
  new ones and loads the conversation.
- **Presets are personal.** A preset is someone's saved harness, account, model, and thinking level:
  a shortcut that starts a choice, which every picker still changes. It shows wherever its account works, and hides once the account is removed.
  A person keeps at most 20.
- **Every model call through the gateway is recorded,** once its answer reached the agent: the
  gateway reads the usage the answer reports as it passes (`ModelUsageReader`, in the WebApi), and
  the call is priced by what's known of the model at the chat's account
  (`IAgentAccountsApi.GetModelFactsAsync`), or by the vendor's own figure, and put in the turn the
  chat was on when the call started. A chat's usage (`GetUsageAsync`) adds them up; how full the
  context is comes from the agent itself where it says (`usage_update`), and from its largest
  latest call otherwise. A Claude plan's agent holds its token, so its calls aren't recorded. What an
  answer's headers say of the account's plan goes to its account (`ObservePlanUsageAsync`).
- **No model key or plan in a nook.** An agent gets the model gateway's URL and a token for its chat;
  the gateway forwards each call with the headers that pay for it. A Claude plan's token, tied to
  Claude Code, goes to the harness itself.
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
- **A list watch wakes only when what lists show changes:** a message saved, or a turn started or
  ended, never for an agent's streamed updates. It then reads the newest 100 chats again and sends
  the ones that differ, so a watcher costs one query per change and holds 100 summaries at most.
- **How long people wait is measured:** `aisloth.chats.first_action`, from a message sent to the
  agent's first action for it.
