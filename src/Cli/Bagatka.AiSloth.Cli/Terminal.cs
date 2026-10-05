using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// The person at the keyboard: what sloth reads from them and writes to them. Interactive when both ends
// are a console, so prompts can wait for typing and secrets are read without echo; otherwise input
// comes from a file or another program, a line at a time.
internal sealed class Terminal(TextReader input, TextWriter output, TextWriter error, bool interactive)
{
    public bool Interactive => interactive;

    // Null at the end of input. A console read can't be cancelled, so it runs on its own thread and
    // cancellation abandons it; it ends with the process, which follows soon after.
    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        return await Task.Run(input.ReadLine, ct).WaitAsync(ct);
    }

    // Everything until the end of input, such as a file piped in.
    public async Task<string> ReadToEndAsync(CancellationToken ct)
    {
        return await Task.Run(input.ReadToEnd, ct).WaitAsync(ct);
    }

    // A secret, typed without echo at a console; otherwise the first line of input, such as from
    // `echo $KEY | sloth secret set NAME`. Ctrl+C cancels the typing.
    public async Task<string?> ReadSecretAsync(string prompt, CancellationToken ct)
    {
        if (!interactive)
        {
            return await ReadLineAsync(ct);
        }

        await WriteAsync(prompt + " (hidden): ");
        StringBuilder typed = new StringBuilder();
        Console.TreatControlCAsInput = true;
        try
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            while (key.Key != ConsoleKey.Enter)
            {
                if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    throw new OperationCanceledException(ct);
                }

                if (key.Key == ConsoleKey.Backspace && typed.Length > 0)
                {
                    typed.Length--;
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    typed.Append(key.KeyChar);
                }

                key = Console.ReadKey(intercept: true);
            }
        }
        finally
        {
            Console.TreatControlCAsInput = false;
            await WriteLineAsync();
        }

        return typed.ToString();
    }

    public async Task WriteLineAsync(string line = "")
    {
        await output.WriteLineAsync(line);
    }

    public async Task WriteAsync(string text)
    {
        await output.WriteAsync(text);
    }

    // Why a command failed, as a sentence on standard error, with what to do about it where there is
    // something to do.
    public async Task FailAsync(string message)
    {
        await error.WriteLineAsync(message);
    }
}
