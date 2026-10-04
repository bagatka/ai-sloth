using System.Globalization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// An account's name for people: trimmed, 1 to 64 characters.
internal sealed record AgentAccountName
{
    public const int MaxLength = 64;

    private AgentAccountName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<AgentAccountName> Parse(string? input)
    {
        string trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > MaxLength)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxLength} characters.");
            return new Result<AgentAccountName>(Error.Validation("name", message));
        }

        return new Result<AgentAccountName>(new AgentAccountName(trimmed));
    }
}
