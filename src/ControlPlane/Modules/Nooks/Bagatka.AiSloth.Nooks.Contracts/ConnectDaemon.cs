using System.Collections.Generic;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Input to <see cref="INookDaemonsApi.ConnectAsync"/>: who the daemon says it is, and what it is running.
/// </summary>
/// <param name="NookId">The nook the daemon runs in.</param>
/// <param name="Token">The secret this module issued for the nook when it was created.</param>
/// <param name="DaemonVersion">The daemon's version, for compatibility checks and diagnostics.</param>
/// <param name="RunningProcesses">
/// The processes still running, so the control plane picks them up again after a reconnect.
/// </param>
public sealed record ConnectDaemon(
    NookId NookId,
    string Token,
    string DaemonVersion,
    IReadOnlyList<RunningProcess> RunningProcesses);
