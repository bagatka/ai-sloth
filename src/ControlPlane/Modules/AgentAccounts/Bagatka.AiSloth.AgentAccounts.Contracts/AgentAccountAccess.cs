namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// How an agent reaches its model on an account: through the model gateway to an endpoint, or with a
/// token its harness holds.
/// </summary>
public union AgentAccountAccess(ModelEndpoint, HarnessToken);
