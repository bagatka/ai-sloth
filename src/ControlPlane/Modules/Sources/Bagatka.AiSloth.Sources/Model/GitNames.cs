using System;
using System.Buffers;
using System.Linq;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Sources.Model;

// What git accepts as an identity and a branch name, kept narrower than git's own rules so every name
// is also safe on GitHub, in URLs, and as one argument.
internal static class GitNames
{
    public const int MaxNameLength = 100;
    public const int MaxEmailLength = 254;
    public const int MaxBranchLength = 200;
    public const int MaxPrefixLength = 50;

    private static readonly SearchValues<char> NotInNames = SearchValues.Create("<>\n\r\0");
    private static readonly SearchValues<char> NotInEmails = SearchValues.Create("<> \n\r\t\0");

    public static Error? CheckIdentity(GitIdentity identity, string field)
    {
        bool validName = identity.Name is { Length: > 0 and <= MaxNameLength } && !identity.Name.AsSpan().ContainsAny(NotInNames) && identity.Name.Trim().Length == identity.Name.Length;
        if (!validName)
        {
            return Error.Validation(field + ".name", "Must be 1 to 100 characters, without <, >, line breaks, or spaces around it.");
        }

        bool validEmail = identity.Email is { Length: > 2 and <= MaxEmailLength } && identity.Email.Contains('@', StringComparison.Ordinal) && !identity.Email.AsSpan().ContainsAny(NotInEmails);
        return validEmail ? null : Error.Validation(field + ".email", "Must be an email address.");
    }

    // A branch name: letters, digits, and . _ - /, in parts separated by single slashes, none starting
    // with a dot or a dash or ending with .lock.
    public static bool IsBranch(string? name)
    {
        if (name is not { Length: > 0 and <= MaxBranchLength } || name[^1] == '/' || name.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return name.Split('/').All(part => part.Length > 0
            && part[0] is not ('.' or '-')
            && !part.EndsWith(".lock", StringComparison.Ordinal)
            && part.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'));
    }

    // A prefix is empty, or a branch name's start: what IsBranch accepts, possibly ending with a slash.
    public static bool IsPrefix(string? prefix)
    {
        return prefix is not null && prefix.Length <= MaxPrefixLength && (prefix.Length == 0 || IsBranch(prefix.TrimEnd('/') + "/x"));
    }
}
