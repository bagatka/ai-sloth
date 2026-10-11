using System;
using System.Collections.Generic;
using System.Globalization;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// What a ChatGPT plan's model call reported of its limits in its answer's headers, as Codex reads them
/// (<c>codex-rs/codex-api/src/rate_limits.rs</c>): a short window and a long one, each with the percent
/// used, and the plan's credits.
/// </summary>
/// <param name="Primary">The short window, such as five hours.</param>
/// <param name="Secondary">The long window, such as a week.</param>
/// <param name="Credits">The plan's credits, when it reports them.</param>
public sealed record ChatGptPlanLimits(ChatGptWindow? Primary, ChatGptWindow? Secondary, ChatGptCredits? Credits)
{
    /// <summary>The limits the headers report (<c>x-codex-*</c>, case-insensitive), or null when they report none.</summary>
    public static ChatGptPlanLimits? Read(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ChatGptWindow? primary = Window(headers, "primary");
        ChatGptWindow? secondary = Window(headers, "secondary");
        bool? hasCredits = Flag(headers, "x-codex-credits-has-credits");
        bool? unlimited = Flag(headers, "x-codex-credits-unlimited");
        ChatGptCredits? credits = hasCredits is bool has && unlimited is bool without
            ? new ChatGptCredits(has, without, Value(headers, "x-codex-credits-balance"))
            : null;
        return primary is null && secondary is null && credits is null ? null : new ChatGptPlanLimits(primary, secondary, credits);
    }

    // A window with something in it: a percent used, a length, or a reset time.
    private static ChatGptWindow? Window(IReadOnlyDictionary<string, string> headers, string which)
    {
        bool used = decimal.TryParse(Value(headers, "x-codex-" + which + "-used-percent"), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal percent);
        if (!used)
        {
            return null;
        }

        bool lasting = int.TryParse(Value(headers, "x-codex-" + which + "-window-minutes"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes);
        bool resetting = long.TryParse(Value(headers, "x-codex-" + which + "-reset-at"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds);
        bool anything = percent != 0 || (lasting && minutes != 0) || resetting;
        return anything
            ? new ChatGptWindow(percent, lasting ? minutes : null, resetting ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null)
            : null;
    }

    private static bool? Flag(IReadOnlyDictionary<string, string> headers, string name)
    {
        string? value = Value(headers, name);
        return value switch
        {
            "true" or "True" or "1" => true,
            "false" or "False" or "0" => false,
            _ => null,
        };
    }

    private static string? Value(IReadOnlyDictionary<string, string> headers, string name)
    {
        foreach (KeyValuePair<string, string> header in headers)
        {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return header.Value.Trim();
            }
        }

        return null;
    }
}
