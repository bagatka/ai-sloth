using System;
using Bagatka.AiSloth.AgentAccounts.Model;

namespace Bagatka.AiSloth.AgentAccounts.Kinds;

// Where a sign-in stands: signed in with the credential, or waiting, perhaps at a new interval.
internal sealed record SignInStep(Credential? Credential, TimeSpan? Interval = null);
