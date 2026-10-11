using System.Collections.Generic;
using Bagatka.AiSloth.AgentAccounts.Contracts;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>A model a harness can run on with an account, as a chat's model.</summary>
/// <param name="Id">Its ID at the account's vendor, such as <c>openai/gpt-6-luna</c>.</param>
/// <param name="Name">Its name for people, as the vendor gives it.</param>
/// <param name="ThinkingLevels">
/// The harness's thinking levels the model takes, in the harness's words, lightest first; empty when
/// the model doesn't reason, so no level applies.
/// </param>
/// <param name="Facts">What's known of it at the account: its limits, its price, and its cache's lifetime.</param>
public sealed record HarnessModel(string Id, string Name, IReadOnlyList<string> ThinkingLevels, ModelFacts Facts);
