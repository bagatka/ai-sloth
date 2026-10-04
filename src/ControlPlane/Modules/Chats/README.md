# Chats

Planned. A chat is a conversation between people and a coding agent working in one nook. Any
harness that speaks the Agent Client Protocol (ACP) can run it, such as Claude Code, Codex, or
Gemini CLI. Several people can take part in one chat, and a nook can have several chats.

## Owns

- **Data:** chats, their turns and events, the checkpoint recorded after each turn, harness
  profiles, and harness state.
- **Rules:** who may take part in a chat (members of its nook's workspace), whose permissions the
  agent acts with (the person who sent the current message), and which agent operations need a
  human's confirmation.

## Does not own

- Nooks, processes, and checkpoints of files: Nooks. A chat's harness is just a process in its nook.
- What the agent can do in AiSloth: that is the public API, reached through MCP.

## Contract

`IChatsApi` (planned). People start chats in a nook, send messages, answer the agent's permission
requests, and fork a chat from any turn; everyone in a chat sees it live.

## Asks

Nooks (start, watch, feed, and stop the harness process; checkpoints and forks), Workspaces.

## Publishes

Nothing yet.

## Reacts to

Nothing yet.

## Data

Schema `chats`: chats, turns, events, and checkpoint references. Harness state lives in object
storage.

## Background work

None yet.

## Configuration

The harness profiles and the MCP endpoint URL handed to agents, as settings.

## Decisions and constraints

- **ACP over the process primitive.** The harness runs as a nook process with `Complete` output
  retention, and its standard input and output carry ACP. This module is the ACP client. The daemon
  never knows about harnesses, and a control-plane deploy only pauses a conversation for seconds.
- **Every event is saved here.** A chat's history survives its nook being suspended or deleted,
  and agents can search it.
- **Our tools reach every harness the same way.** `session/new` hands the agent the AiSloth MCP
  endpoint with a token for this chat, so it can do whatever the person driving the turn can.
- **Harness features, in three layers.** First, show whatever the harness advertises (slash
  commands, modes, options) without per-harness code. Second, add a per-harness profile only where
  it clearly pays off. Third, offer the harness's own terminal interface in the same nook as the
  escape hatch, so no feature is ever out of reach.
- **Checkpoints and forks.** After every turn, this module asks Nooks to checkpoint the nook's
  files and saves the harness's session state with it. Forking from a turn creates a new nook from
  that checkpoint and resumes the conversation up to that turn: through the harness's own session
  files where it supports that, or else with a new session primed with the transcript. Running
  processes and packages installed outside recipes are not carried over.
- **Harness state.** A harness profile lists the paths where the harness keeps state between
  sessions, such as memory and skills (saved after each turn, restored into new nooks with the
  same sources, newest file wins) and session files (saved with each checkpoint). Credentials and
  caches are never saved. We never read or change the contents, and people can inspect and reset
  them, because a hostile repository could plant instructions there.
- **Checkpoints ship with the first chats.** They are what keeps a lost nook from costing more than
  the turn in progress (`ARCHITECTURE.md`, "Nothing delivered is lost"), so they are not a later
  feature that forks bring.
- **A nearly full disk needs confirmation.** When a nook's disk is 90% used, a new message needs a
  person's explicit confirmation, which says why: the agent may stop mid-task and files it is
  writing can be cut short, while everything up to the last turn is saved. People free space by
  confirming and asking the agent, or through a terminal, which is never blocked. This reuses the
  confirmation people already give to sensitive agent operations.
