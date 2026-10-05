namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>A plan's token that goes to its harness itself, such as a Copilot token. Its text form leaves it out.</summary>
/// <param name="Value">The token.</param>
public sealed record HarnessToken(string Value)
{
    /// <inheritdoc />
    public override string ToString()
    {
        return "HarnessToken { Value = *** }";
    }
}
