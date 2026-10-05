namespace Bagatka.AiSloth.Sources.Git;

// How a git run ended: its exit code, and the start of what it printed.
internal sealed record GitRun(int ExitCode, string Output, string Errors)
{
    public bool Succeeded => ExitCode == 0;
}
