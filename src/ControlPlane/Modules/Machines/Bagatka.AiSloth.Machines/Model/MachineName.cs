using System.Globalization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Machines.Model;

// A machine's display name: trimmed, 1 to 64 characters.
internal sealed record MachineName
{
    public const int MaxLength = 64;

    private MachineName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<MachineName> Parse(string? input)
    {
        string trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > MaxLength)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxLength} characters.");
            return new Result<MachineName>(Error.Validation("name", message));
        }

        return new Result<MachineName>(new MachineName(trimmed));
    }
}
