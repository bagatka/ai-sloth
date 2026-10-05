using System;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// How testing the setup the agent wrote in a fresh nook ended: it ran from scratch, then again, as a ready
/// copy will run it. A failure goes back to the agent to fix, until the third test.
/// </summary>
/// <param name="Test">Which test this was, 1 to 3.</param>
/// <param name="ExitCode">0 when both runs succeeded; otherwise the failing run's, or -1 when it couldn't run or was stopped.</param>
/// <param name="FromScratch">How long the first run took; zero when it couldn't run.</param>
/// <param name="Again">How long the second run took, or <see langword="null"/> when the first failed.</param>
/// <param name="Output">What went wrong, as the end of the failing run's output or in words; <see langword="null"/> when it worked.</param>
/// <param name="AgentFixes">Whether the failure went to the agent to fix, and another test follows its turn.</param>
public sealed record SetupTested(int Test, int ExitCode, TimeSpan FromScratch, TimeSpan? Again, string? Output, bool AgentFixes);
