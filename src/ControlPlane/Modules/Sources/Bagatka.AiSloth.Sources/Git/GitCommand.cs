using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Sources.Git;

// The git command line on the control plane's computer, which moves code between GitHub and nooks.
// A token reaches git only through its environment, as the HTTP header git sends to the host: never in
// arguments, URLs, files, or anything returned. Each run ends within its deadline, killed if need be.
internal static class GitCommand
{
    // Copying a large repository takes minutes; nothing takes longer.
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(15);

    // How much of what git prints is kept: enough to explain a failure, or to read a commit or a ref.
    private const int MaxKeptCharacters = 8000;

    // Runs git in the directory. Standard input comes from `input`, or is empty; standard output goes
    // to `output`, or is returned. Ignores the computer's git configuration, so every run behaves alike.
    public static async Task<GitRun> RunAsync(string directory, IReadOnlyList<string> arguments, string? token, Stream? input, Stream? output, CancellationToken ct)
    {
        ProcessStartInfo start = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = directory,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        start.Environment["LC_ALL"] = "C";
        if (token is not null)
        {
            string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + token));
            start.Environment["GIT_CONFIG_COUNT"] = "1";
            start.Environment["GIT_CONFIG_KEY_0"] = "http.extraHeader";
            start.Environment["GIT_CONFIG_VALUE_0"] = "Authorization: Basic " + basic;
        }

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Deadline);
        using Process process = Started(start);
        try
        {
            Task feeding = FeedAsync(process.StandardInput.BaseStream, input, deadline.Token);
            Task<string> printing = output is null ? KeepAsync(process.StandardOutput, deadline.Token) : CopyAsync(process.StandardOutput.BaseStream, output, deadline.Token);
            Task<string> complaining = KeepAsync(process.StandardError, deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            await feeding;
            string printed = await printing;
            string complaints = await complaining;
            return new GitRun(process.ExitCode, printed.Trim(), complaints.Trim());
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static Process Started(ProcessStartInfo start)
    {
        try
        {
            Process? process = Process.Start(start);
            if (process is null)
            {
                throw new InvalidOperationException("The operating system started no git process.");
            }

            return process;
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("The control plane's computer has no git command line; install git there.", exception);
        }
    }

    // Git may stop reading early, such as when the remote refuses; what's left to send no longer matters.
    private static async Task FeedAsync(Stream standardInput, Stream? input, CancellationToken ct)
    {
        try
        {
            if (input is not null)
            {
                await input.CopyToAsync(standardInput, ct);
            }
        }
        catch (IOException)
        {
            // Git closed its standard input.
        }
        finally
        {
            await standardInput.DisposeAsync();
        }
    }

    private static async Task<string> CopyAsync(Stream standardOutput, Stream output, CancellationToken ct)
    {
        await standardOutput.CopyToAsync(output, ct);
        return string.Empty;
    }

    // Keeps the start of what git prints, up to the limit, and reads the rest so git never blocks.
    private static async Task<string> KeepAsync(StreamReader reader, CancellationToken ct)
    {
        StringBuilder kept = new StringBuilder();
        char[] buffer = new char[4096];
        int read = await reader.ReadAsync(buffer, ct);
        while (read > 0)
        {
            kept.Append(buffer, 0, Math.Min(read, Math.Max(0, MaxKeptCharacters - kept.Length)));
            read = await reader.ReadAsync(buffer, ct);
        }

        return kept.ToString();
    }
}
