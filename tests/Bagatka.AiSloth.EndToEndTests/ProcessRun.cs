namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>What a process wrote to each channel, and its exit code.</summary>
internal sealed record ProcessRun(string StandardOutput, string StandardError, int ExitCode);
