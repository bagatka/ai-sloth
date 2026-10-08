namespace Bagatka.AiSloth.Nooks;

// How a script the control plane ran in a nook ended: its exit code, and the end of what it printed
// to standard error.
internal sealed record ProcessRun(int ExitCode, string Errors)
{
    public bool Succeeded => ExitCode == 0;
}
