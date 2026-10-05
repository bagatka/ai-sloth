using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagatka.AiSloth.Cli;

// A command's words after its name: arguments, options with a value (`--name Alex`), and switches
// (`--workspace`). An unknown option doesn't parse, so a typo never runs a command differently.
internal sealed class CommandLine
{
    private readonly Dictionary<string, string> _values;
    private readonly HashSet<string> _switches;

    private CommandLine(List<string> arguments, Dictionary<string, string> values, HashSet<string> switches)
    {
        Arguments = arguments;
        _values = values;
        _switches = switches;
    }

    public IReadOnlyList<string> Arguments { get; }

    // Null when the words don't fit: an unknown option, an option without its value, or one given twice.
    public static CommandLine? Parse(IReadOnlyList<string> words, IReadOnlyList<string> options, IReadOnlyList<string> switches)
    {
        List<string> arguments = [];
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
        HashSet<string> given = new HashSet<string>(StringComparer.Ordinal);
        for (int at = 0; at < words.Count; at++)
        {
            string word = words[at];
            if (!word.StartsWith("--", StringComparison.Ordinal))
            {
                arguments.Add(word);
            }
            else if (switches.Contains(word, StringComparer.Ordinal) && given.Add(word))
            {
                continue;
            }
            else if (options.Contains(word, StringComparer.Ordinal) && at + 1 < words.Count && values.TryAdd(word, words[at + 1]))
            {
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
        return _values.GetValueOrDefault(option);
    }

    public bool Has(string option)
    {
        return _switches.Contains(option);
    }
}
