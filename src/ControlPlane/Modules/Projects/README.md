# Projects

Planned. A project groups nooks, chats, and sources so a team can work toward one goal together,
like a project in Linear: for example, a feature that spans the UI and the backend. It is an
extension: nooks and chats work on their own, and deleting this module would leave them all
working as single units.

## Owns

- **Data:** projects, their members, the nooks, chats, and sources they group, and their shared
  context (instructions every chat in the project receives).
- **Rules:** who belongs to a project, and what its project chat may do.

## Does not own

- Nooks, chats, and sources: their modules. A project only refers to them by ID.

## Contract

`IProjectsApi` (planned). Members create projects, add nooks, chats, and sources, start new nooks
with the project's sources, and talk to the project chat, whose agent can see every nook in the
project.

## Asks

Nooks, Chats, Sources, Workspaces.

## Publishes

Nothing.

## Reacts to

`NookDeleted` and `ChatDeleted` (planned), to drop them from projects.

## Data

Schema `projects`: projects, members, and the IDs of what they group.

## Background work

None.

## Configuration

None.

## Decisions and constraints

- **Nothing depends on Projects.** No module asks it or reacts to its events, and no other contract
  mentions a project. It composes general operations: it starts nooks with the project's sources,
  passes its shared context to new chats as ordinary chat instructions, and gives its project chat
  the tools every agent has. An architecture test will enforce this once the module exists.
