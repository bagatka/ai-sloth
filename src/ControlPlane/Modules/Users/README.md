# Users

Users are the people who sign in. A user is whoever an identity provider vouches for, known by the
provider's issuer and its subject for that person, and recorded on their first sign-in.

## Owns

- **Data:** users and the identity each signs in with.
- **Rules:** which identity is which user, and that only system code may vouch for an identity.

## Does not own

- Validating tokens: the WebApi checks the provider's signature, issuer, and audience before it asks
  this module who the subject is.
- Workspaces and membership: Workspaces.

## Contract

`IUsersApi` in `Bagatka.AiSloth.Users.Contracts`. The WebApi's authentication turns each validated
token into a `UserId` with `SignInAsync`, as the system actor `webapi.authentication`; users read
themselves with `GetMeAsync`.

## Asks

Nothing.

## Publishes

Nothing yet. `UserRegistered` comes with its first consumer, a personal workspace for every user.

## Reacts to

Nothing.

## Data

Schema `users`. Table `users` (ID, issuer, subject, created at), unique per issuer and subject.

## Background work

None.

## Configuration

`UsersSettings`: the connection string.

## Decisions and constraints

- **Any provider, no provider code.** The issuer and subject are opaque strings, so WorkOS, Entra ID,
  Cognito, or a self-hoster's provider all work the same, and changing providers means new users
  until accounts can be linked.
- **Two first sign-ins at once** (a web app's parallel requests) both get the same user: the unique
  index lets one insert win, and the other reads what it wrote.
- **Every request looks the user up.** One indexed query; cache it only when measurements ask.

## Not built yet

- Profiles (names, emails), account linking, and deleting users.
