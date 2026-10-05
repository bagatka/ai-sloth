using System;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// What <see cref="ChatGptSignInClient.AuthorizationUrl"/> asks the person's browser to authorize.
/// </summary>
/// <param name="ClientId">
/// <see cref="ChatGptSignInClient.RegistrationClientId"/> for a first sign-in, which registers the app
/// for the person's ChatGPT account; afterwards the client ID that registration issued.
/// </param>
/// <param name="AgentNameHint">The app's name, suggested to the person on a first sign-in; <see langword="null"/> afterwards.</param>
/// <param name="HostId">The installation's stable identifier, such as <c>urn:uuid:…</c>.</param>
/// <param name="RedirectUri">Where the browser returns: <c>http://127.0.0.1:&lt;port&gt;/auth/callback</c> (<see cref="ChatGptSignInClient.AcceptsRedirect"/>).</param>
/// <param name="State">A fresh random value the callback must return.</param>
/// <param name="Nonce">A fresh random value the ID token must carry.</param>
/// <param name="CodeChallenge">The PKCE challenge of a fresh verifier (<see cref="ChatGptSignInClient.CodeChallenge"/>).</param>
public sealed record ChatGptAuthorization(
    string ClientId,
    string? AgentNameHint,
    string HostId,
    Uri RedirectUri,
    string State,
    string Nonce,
    string CodeChallenge);
