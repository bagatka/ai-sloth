using System;
using System.Collections.Generic;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// A nook's setup: scripts that come with its files and prepare it, which run whenever the nook gets
/// its files, as a new nook, a copy, or one brought back after its sandbox was lost. Every
/// <c>.agents/setup</c> runs first, installing what the code needs, then every <c>.agents/resume</c>,
/// starting its services; those at the top of the files before each folder's directly in it, by
/// name, each in its own folder, with the workspace's secrets. A setup may take 30 minutes and
/// a resume 5. Their output is also written to <c>/var/log/aisloth/setup.log</c> in the nook.
/// </summary>
/// <param name="Scripts">The scripts, as paths in the nook's files, in the order they run; empty when there are none.</param>
/// <param name="Run">Their latest run, or <see langword="null"/> when there are no scripts.</param>
/// <param name="ReadyCopyMadeAt">
/// When the ready copy the latest run set the nook up from was made: a copy of a nook with the same
/// files right after a setup that took a while, which leaves this setup little to do. <see langword="null"/>
/// when the run set it up from scratch, or only resumed it.
/// </param>
public sealed record NookSetup(IReadOnlyList<string> Scripts, SetupRun? Run, DateTimeOffset? ReadyCopyMadeAt);
