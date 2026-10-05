using System;
using System.Globalization;

namespace Bagatka.Harnesses;

// The IDs of the requests this client sends. A prompt's or steer's ID carries the caller's key, so a
// response read later, even from saved output by another process, still finds what it answers. Hosts
// keep harness output, so this format is persisted: changing it breaks reading output written before.
internal static class RequestIds
{
    public const string Initialize = "initialize";
    public const string NewSession = "session/new";
    public const string LoadSession = "session/load";
    private const string PromptPrefix = "prompt:";
    private const string SteerPrefix = "steer:";

    public static string Prompt(Guid key)
    {
        return PromptPrefix + key.ToString("D", CultureInfo.InvariantCulture);
    }

    public static string Steer(Guid key)
    {
        return SteerPrefix + key.ToString("D", CultureInfo.InvariantCulture);
    }

    public static Guid? Prompted(string id)
    {
        return KeyOf(id, PromptPrefix);
    }

    public static Guid? Steered(string id)
    {
        return KeyOf(id, SteerPrefix);
    }

    private static Guid? KeyOf(string id, string prefix)
    {
        if (!id.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        bool parsed = Guid.TryParseExact(id[prefix.Length..], "D", out Guid key);
        return parsed ? key : null;
    }
}
