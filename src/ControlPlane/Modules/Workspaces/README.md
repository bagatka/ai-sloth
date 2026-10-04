# Workspaces

Workspaces are where people work together and what owns nooks, like Slack workspaces. A user
can be a member of many, such as a personal one and a company one.

## Owns

- **Data:** workspaces and their members.
- **Rules:** who is a member, with which role, and who may manage members (owners).

## Does not own

- Users and sign-in: Users (planned).
- What a role allows for another module's data: each module decides, using `GetRoleAsync`.

## Contract

`IWorkspacesApi` in `Bagatka.AiSloth.Workspaces.Contracts`. Users create workspaces and list their
own; other modules ask for an actor's role before applying their own permission rules.

## Asks

Nothing.

## Publishes

Nothing yet.

## Reacts to

Nothing yet. A personal workspace for every new user will react to Users' `UserRegistered`.

## Data

Schema `workspaces`. Tables `workspaces` (ID, name, created at) and `members` (workspace ID, user
ID, role), unique per workspace and user.

## Background work

None.

## Configuration

`WorkspacesSettings`: the connection string.

## Decisions and constraints

- **Membership is the only permission data.** The contract answers "what is this actor's role
  here?"; it never answers "may this actor do X?", because X belongs to another module.

## Not built yet

- Invitations, removing members, and transferring ownership: not designed yet. A workspace has
  exactly the member who created it.
- A personal workspace for every new user: waits for Users' `UserRegistered`, which needs the outbox.
