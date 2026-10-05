using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Model;

// A process the control plane asked a nook's daemon to run.
internal sealed class Process
{
    // The longest command or working directory the operating system accepts (PATH_MAX).
    public const int MaxPathLength = 4096;

    // Used by Start and by EF: parameter names match property names.
    private Process(ProcessId id, NookId nookId, string command, List<string> arguments, string? workingDirectory, OutputRetention retention, DateTimeOffset startedAt)
    {
        Id = id;
        NookId = nookId;
        Command = command;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        Retention = retention;
        StartedAt = startedAt;
    }

    public ProcessId Id { get; private set; }

    public NookId NookId { get; private set; }

    public string Command { get; private set; }

    public List<string> Arguments { get; private set; }

    public string? WorkingDirectory { get; private set; }

    public OutputRetention Retention { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public int? ExitCode { get; private set; }

    public static Result<Process> Start(StartProcess command, TimeProvider time)
    {
        string tooLong = string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxPathLength} characters without NUL characters.");
        if (!IsPath(command.Command))
        {
            return new Result<Process>(Error.Validation("command", tooLong));
        }

        if (command.WorkingDirectory is not null && !IsPath(command.WorkingDirectory))
        {
            return new Result<Process>(Error.Validation("workingDirectory", tooLong));
        }

        // The operating system ends strings at NUL, so it could never receive such an argument.
        if (command.Arguments.Any(argument => argument.Contains('\0', StringComparison.Ordinal)))
        {
            return new Result<Process>(Error.Validation("arguments", "Arguments can't contain NUL characters."));
        }

        bool environmentValid = command.Environment.All(variable => IsVariable(variable.Key, variable.Value));
        if (!environmentValid)
        {
            return new Result<Process>(Error.Validation("environment", "Names can't be empty, contain = or NUL, or start with SLOTHD_; values can't contain NUL."));
        }

        if (command.Retention is not (OutputRetention.Recent or OutputRetention.Complete))
        {
            return new Result<Process>(Error.Validation("retention", "Must be Recent or Complete."));
        }

        return new Result<Process>(new Process(
            ProcessId.New(),
            command.NookId,
            command.Command,
            [.. command.Arguments],
            command.WorkingDirectory,
            command.Retention,
            time.GetUtcNow()));
    }

    // The daemon reports exits again after every reconnect; the first report counts.
    public void Exited(int exitCode)
    {
        ExitCode ??= exitCode;
    }

    // The environment is passed through, never kept: it may hold secrets.
    public StartProcessInstruction ToInstruction(IReadOnlyDictionary<string, string> environment, bool inputStreamed)
    {
        return new StartProcessInstruction(Id, Command, Arguments, WorkingDirectory, Retention, environment, inputStreamed);
    }

    public ProcessSummary ToSummary()
    {
        return new ProcessSummary(Id, NookId, Command, Arguments, StartedAt, ExitCode);
    }

    // An environment string is NAME=value up to a NUL, and SLOTHD_ names belong to the daemon.
    private static bool IsVariable(string name, string value)
    {
        bool validName = name.Length > 0 && !name.AsSpan().ContainsAny('=', '\0') && !name.StartsWith("SLOTHD_", StringComparison.Ordinal);
        bool validValue = !value.Contains('\0', StringComparison.Ordinal);
        return validName && validValue;
    }

    private static bool IsPath(string value)
    {
        return value.Length is > 0 and <= MaxPathLength && !value.Contains('\0', StringComparison.Ordinal);
    }
}
