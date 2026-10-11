using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts.Kinds;

// A plan: a person's subscription, always theirs, added by signing in at its vendor, which reports what
// it has used of its limits. A sign-in starts (StartSignInAsync) and is continued until the person has
// approved it (ContinueSignInAsync); a credential with a refresh token is renewed before it expires.
internal interface IPlanKind : IAccountKind
{
    // What the person opens, and enters there if anything, and the secret the sign-in continues with.
    public Task<PendingSignIn> StartSignInAsync(SignInId id, CancellationToken ct);

    // The credential once the person approved; until then the sign-in waits, perhaps asked to ask less
    // often. Fails when the vendor refuses, or the code the person brought back is wrong.
    public Task<Result<SignInStep>> ContinueSignInAsync(SignInId id, string? userCode, string secret, string? code, CancellationToken ct);

    // The credential renewed; fails when the vendor ended the sign-in. Only a credential with a
    // refresh token is renewed, and never twice at once: a renewal replaces the refresh token.
    public Task<Result<Credential>> RenewAsync(Credential credential, CancellationToken ct);

    // What the plan has used of its limits, asked of its vendor now; throws when it can't be reached.
    public Task<IReadOnlyList<UsageMeter>> MetersAsync(Credential credential, CancellationToken ct);
}
