using System;

namespace Bagatka.AiSloth.AgentAccounts.Kinds;

// A sign-in as its vendor started it: where the person goes, the code they enter there if any, how
// long it lasts and how often the vendor may be asked, and the secret it continues with.
internal sealed record PendingSignIn(Uri Url, string? UserCode, TimeSpan ExpiresIn, TimeSpan Interval, string Secret);
