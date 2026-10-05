using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagatka.AiSloth.Cli;

// A command's words after its name: arguments, options with a value (`--name Alex`), and switches
// (`--workspace`). Options listed as repeatable may come more than once (`--repo api --repo web`);
// others only once. An unknown option doesn't parse, so a typo never runs a command differently.
internal sealed class CommandLine
{
    private readonly Dictionary<string, List<string>> _values;
    private readonly HashSet<string> _switches;

    private CommandLine(List<string> arguments, Dictionary<string, List<string>> values, HashSet<string> switches)
    {
        Arguments = arguments;
        _values = values;
        _switches = switches;
    }

    public IReadOnlyList<string> Arguments { get; }

    // Null when the words don't fit: an unknown option, an option without its value, or one given
    // twice that isn't repeatable.
    public static CommandLine? Parse(IReadOnlyList<string> words, IReadOnlyList<string> options, IReadOnlyList<string> switches, IReadOnlyList<string>? repeatable = null)
    {
        List<string> arguments = [];
        Dictionary<string, List<string>> values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        HashSet<string> given = new HashSet<string>(StringComparer.Ordinal);
        for (int at = 0; at < words.Count; at++)
        {
            string word = words[at];
            bool once = options.Contains(word, StringComparer.Ordinal);
            bool many = repeatable?.Contains(word, StringComparer.Ordinal) == true;
            if (!word.StartsWith("--", StringComparison.Ordinal))
            {
                arguments.Add(word);
            }
            else if (switches.Contains(word, StringComparer.Ordinal) && given.Add(word))
            {
                continue;
            }
            else if ((once || many) && at + 1 < words.Count && (many || !values.ContainsKey(word)))
            {
                List<string> list = values.GetValueOrDefault(word) ?? [];
                list.Add(words[at + 1]);
                values[word] = list;
                at++;
            }
            else
            {
                return null;
            }
        }

        return new CommandLine(arguments, values, given);
    }

    public string? Value(string option)
    {
        return _values.GetValueOrDefault(option)?.LastOrDefault();
    }

    public IReadOnlyList<string> Values(string option)
    {
        return _values.GetValueOrDefault(option) ?? [];
    }

    public bool Has(string option)
    {
        return _switches.Contains(option);
    }
}
