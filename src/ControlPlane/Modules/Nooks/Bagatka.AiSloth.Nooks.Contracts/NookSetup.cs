using System.Collections.Generic;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// A nook's setup: the project's own scripts that prepare it, which run whenever the nook gets its
/// files, as a new nook, a copy, or one brought back after its sandbox was lost. Every
/// <c>.agents/setup</c> runs first, installing what the project needs, then every
/// <c>.agents/resume</c>, starting its services; <c>/work</c>'s own before each folder's directly in
/// it, by name, each in its own folder, with the workspace's secrets. A setup may take 30 minutes and
/// a resume 5. Their output is also written to <c>/var/log/aisloth/setup.log</c> in the nook.
/// </summary>
/// <param name="Scripts">The scripts, as paths relative to <c>/work</c>, in the order they run; empty when the project has none.</param>
/// <param name="Run">Their latest run, or <see langword="null"/> when there are no scripts.</param>
public sealed record NookSetup(IReadOnlyList<string> Scripts, SetupRun? Run);
