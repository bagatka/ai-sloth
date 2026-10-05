using System;
using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Chats.Harness;

// How testing a setup in a fresh nook ended (SetupTested).
internal sealed record SetupTestResult(int ExitCode, TimeSpan FromScratch, TimeSpan? Again, string? Output)
{
    public static SetupTestResult CouldNotRun(string why)
    {
        return new SetupTestResult(ProcessExited.Lost, TimeSpan.Zero, Again: null, why);
    }
}
