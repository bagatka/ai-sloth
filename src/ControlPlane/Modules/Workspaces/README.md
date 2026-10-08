# Workspaces

Workspaces are where people work together and what owns nooks, like Slack workspaces, and this
module decides who may do what with them. People are given an access level, Read, Write, or Manage,
on a resource: a workspace, or one nook in it. Access to a resource reaches everything in it, so a
workspace's editors write in all its nooks, while a nook's guest works in that nook alone. People join
through one-time invites.

## Owns

- **Data:** workspaces; grants (a person's access level on a resource); which resource is in which
  (a nook in its workspace); invites.
- **Rules:** a person's access to a resource is the highest level given to them on it or on anything
  it is in; managers invite people and end their access; a workspace keeps at least one manager; an
  invite works once, for 7 days.

## Does not own

- Users and sign-in: Users.
- What a level allows for another module's data: each module decides, comparing the level from
  `GetAccessAsync` with what a feature needs. Read sees, Write works, Manage decides who else has
  access and manages the resource itself.
- Nooks themselves: Nooks registers each new nook here (`AddResourceAsync`), so access to the
  workspace reaches it.

## Contract

`IWorkspacesApi` in `Bagatka.AiSloth.Workspaces.Contracts`: people create workspaces and list theirs;
other modules ask for an actor's access level on a resource; managers invite people and end their
access; anyone signed in accepts an invite.

```csharp
AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Nook(nookId.Value), ct);
if (access is null) return new Result(NooksErrors.NotFound);   // they may not even see it
if (access < AccessLevel.Write) return new Result(Error.Forbidden);

Result<Invite> invite = await workspaces.InviteAsync(manager, new CreateInvite(Resource.Workspace(workspaceId), AccessLevel.Write), ct);
// invite.Output.Code goes to the person, who accepts it: AcceptInviteAsync(person, new AcceptInvite(code))
```

## Asks

Nothing.

## Publishes

Nothing yet.

## Reacts to

Nothing yet. A personal workspace for every new user will react to Users' `UserRegistered`.

## Data

Schema `workspaces`. Tables `workspaces` (ID, name, created at), `grants` (resource kind and ID,
user ID, access level; one per resource and person), `links` (a child resource and the parent it is
in), and `invites` (code hash, resource, access level, who created it, when it expires, who accepted
it and when; `xmin` as concurrency token).

## Background work

None.

## Configuration

None; the host passes the shared database.

## Decisions and constraints

- **Access is the only permission data.** The contract answers "how much may this actor do with
  this resource?"; it never answers "may this actor do X?", because X belongs to another module.
- **One rule for every resource.** Projects will be another kind of resource that nooks are in, and
  teams will be grants to a group instead of a person; neither changes how callers ask.
- **Resources are plain data.** Workspaces can't reference Nooks, which depends on it, so a
  resource is a kind and an ID. IDs are UUIDv7s, so grants match resources by ID alone.
- **Not found before forbidden.** Someone with no access learns nothing, not even that a resource
  exists; someone who may see it but not do something gets forbidden.
- **Invite codes** are one-time codes (`OneTimeCode` in Foundation), kept only as hashes and sent in
  request bodies, never in URLs.
