namespace Bagatka.AiSloth.AgentAccounts;

/// <summary>
/// What the AgentAccounts module needs from its host: which plans it allows. Where the vendors are is
/// <c>Bagatka.ModelAccess</c>'s settings.
/// </summary>
/// <param name="AllowClaudePlans">
/// Whether people may add Claude plans, signed in as Claude Code. Anthropic lets apps built on its Agent
/// SDK use a person's own plan; a service that offers it to other people should agree it with
/// Anthropic first.
/// </param>
/// <param name="AllowChatGptPlans">
/// Whether people may add ChatGPT plans, signed in as Codex. A service that offers it to other people
/// should agree it with OpenAI first.
/// </param>
public sealed record AgentAccountsSettings(bool AllowClaudePlans = false, bool AllowChatGptPlans = false);
