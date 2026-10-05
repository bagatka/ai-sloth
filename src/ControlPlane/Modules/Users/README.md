# Users

Users are the people who use a host, by name, and the devices they're signed in on. People prove who
they are with a code or through the host's sign-in provider; then each device carries a session the
host issued.

## Owns

- **Data:** users (name, and the provider identity of those who signed in with one), sessions, and
  sign-in codes.
- **Rules:** which provider identity is which user; that a setup code makes its user the host's
  first person only while nobody has signed up; that each code works once, before it expires; which
  token is whose session, and when a session ends; and that only system code may start sessions, the
  only way to vouch for an identity or admit a newcomer.

## Does not own

- Signing in with a provider: the WebApi is the provider's client and validates its ID tokens
  (`SignInProvider`), then asks this module who the person is.
- Invites, and whether one lets someone new sign up: Workspaces checks the invite; the WebApi decides
  whether the host lets invites sign people up.
- Workspaces, including the one someone new gets: Workspaces, asked by the WebApi.

## Contract

`IUsersApi` in `Bagatka.AiSloth.Users.Contracts`. People use it for themselves: their profile,
others' names, a link code for another device, and their sessions. The WebApi's sign-in and
authentication use it as system code, never as a route: `SignInAsync` turns a proof of who someone is
(a provider's verified identity, a setup or link code, or a newcomer an invite admits) into a session,
and `AuthenticateAsync` tells whose a token is.

```csharp
SignIn withCode = new SignIn(new SignInProof(new SignInCode(code, "Alex")), "sloth on laptop");
Result<StartedSession> session = await users.SignInAsync(Actor.ForSystem("webapi.sign-in"), withCode, ct);
// UsersErrors.CodeNotFound: the code is unknown, used, or expired.
// session.Output.Token goes to the device once; each call presents it.
UserId? caller = await users.AuthenticateAsync(token, ct);
```

## Asks

Nothing.

## Publishes

Nothing yet.

## Reacts to

Nothing.

## Data

Schema `users`. Table `users` (ID, name, issuer and subject for someone who signed in with a provider,
created at), unique per issuer and subject; a check keeps issuer and subject both set or both empty.
Table `sessions` (ID, user, device name, token hash, started at, last used at), unique per token hash.
Table `issued_codes` (ID, purpose: setup or link, user for a link code, code hash, expires at),
unique per code hash. A code is removed in the same transaction that starts its session.

## Background work

None. Expired codes are deleted when new ones are made; sessions end when they go unused.

## Configuration

`UsersSettings`: the connection string.

## Decisions and constraints

- **The host issues sessions; a provider is optional.** Every client (sloth, the web app, the mobile
  apps) holds the same kind of opaque session token, so no client knows about providers, and a host
  without one signs people in with codes alone.
- **Secrets kept as hashes.** Session tokens (`aisloth_` and 32 random bytes) and codes
  (`OneTimeCode`) are stored only as SHA-256 hashes, and shown once.
- **Sessions end after 90 days unused,** or when their person ends them. Each use is recorded at most
  once a day, so authentication costs one indexed read.
- **A setup code while nobody has signed up.** The WebApi asks for one at startup and prints it to
  standard output only; it works for a day, once, and stops working when anyone signs up.
- **Link codes** work once within 10 minutes, for their creator only; someone who lost every device
  asks the host's owner for an invite.
- **Any provider, no provider code.** The issuer and subject are opaque strings, so WorkOS, Entra ID,
  or a self-hoster's provider all work the same, and changing providers means new users until
  accounts can be linked.
- **Two first sign-ins at once** (a web app's parallel requests) both get the same user: the unique
  index lets one insert win, and the other reads what it wrote.

## Not built yet

- Emails, renaming, account linking, and deleting users.
- Ending every session of a person at once, and sessions that expire regardless of use.
