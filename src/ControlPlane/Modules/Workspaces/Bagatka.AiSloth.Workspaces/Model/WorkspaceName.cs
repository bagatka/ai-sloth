using System.Globalization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Workspaces.Model;

// A workspace's display name: trimmed, 1 to 100 characters.
internal sealed record WorkspaceName
{
    public const int MaxLength = 100;

    private WorkspaceName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<WorkspaceName> Parse(string? input)
    {
        string trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > MaxLength)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxLength} characters.");
            return new Result<WorkspaceName>(Error.Validation("name", message));
        }

        return new Result<WorkspaceName>(new WorkspaceName(trimmed));
    }
}
