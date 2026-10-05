using System.Globalization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Users.Model;

// A name for people: a person's or a device's, trimmed, 1 to 100 characters.
internal sealed record BoundedName
{
    public const int MaxLength = 100;

    private BoundedName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    // A person's name when nothing better is known.
    public static BoundedName NewPerson { get; } = new BoundedName("New person");

    public static Result<BoundedName> Parse(string? input, string field)
    {
        string trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > MaxLength)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxLength} characters.");
            return new Result<BoundedName>(Error.Validation(field, message));
        }

        return new Result<BoundedName>(new BoundedName(trimmed));
    }
}
