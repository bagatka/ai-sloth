using System;
using System.Linq;

namespace Bagatka.ObjectStorage;

/// <summary>What a valid object key is.</summary>
public static class ObjectKeys
{
    /// <summary>The longest key.</summary>
    public const int MaxLength = 512;

    /// <summary>Whether <paramref name="key"/> is segments of lowercase letters, digits, <c>.</c>, <c>_</c>, and <c>-</c>, separated by single <c>/</c>, none of them <c>.</c> or <c>..</c>.</summary>
    public static bool IsValid(string? key)
    {
        return key is { Length: > 0 and <= MaxLength }
            && key.Split('/').All(segment => segment.Length > 0
                && segment is not ("." or "..")
                && segment.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '.' or '_' or '-'));
    }

    /// <summary>Throws unless <paramref name="key"/> is valid.</summary>
    public static void Check(string key, string parameter)
    {
        if (!IsValid(key))
        {
            throw new ArgumentException("An object key is segments of a-z, 0-9, '.', '_', and '-', separated by '/'.", parameter);
        }
    }
}
