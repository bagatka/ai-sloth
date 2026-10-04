namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Something the control plane tells a nook's daemon to do.
/// </summary>
public union DaemonInstruction(
    StartProcessInstruction,
    StopProcessInstruction,
    SendInputInstruction,
    WatchOutputInstruction,
    ReconnectInstruction);
