using System;
using System.Buffers;
using System.Globalization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Secrets.Model;

// An environment variable's name: letters, digits, and underscores, not starting with a digit, at
// most 128 characters. Names the system's own processes rely on are refused, so a secret can't break
// every process in a nook: PATH, HOME, the dynamic linker's LD_*, and the daemon's SLOTHD_*.
internal sealed record SecretName
{
    public const int MaxLength = 128;

    private static readonly SearchValues<char> Allowed = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_");

    private SecretName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<SecretName> Parse(string? input)
    {
        string name = input ?? string.Empty;
        bool shaped = name.Length is > 0 and <= MaxLength
            && !char.IsAsciiDigit(name[0])
            && name.AsSpan().IndexOfAnyExcept(Allowed) < 0;
        if (!shaped)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxLength} letters, digits, and underscores, not starting with a digit.");
            return new Result<SecretName>(Error.Validation("name", message));
        }

        bool systems = name is "PATH" or "HOME"
            || name.StartsWith("LD_", StringComparison.Ordinal)
            || name.StartsWith("SLOTHD_", StringComparison.Ordinal);
        if (systems)
        {
            return new Result<SecretName>(Error.Validation("name", "PATH, HOME, LD_*, and SLOTHD_* are the system's own."));
        }

        return new Result<SecretName>(new SecretName(name));
    }
}
