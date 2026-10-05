namespace Bagatka.AiSloth.AgentAccounts.Model;

// What the vendor's callback brought: the code to exchange and the client ID the registration issued.
internal sealed record SignInAnswer(string Code, string ClientId);
