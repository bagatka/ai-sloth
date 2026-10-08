namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Something a nook's daemon reports on its control connection: a process exited, or what the nook
/// uses (on connecting, every 30 seconds, and whenever the disk fills up).
/// </summary>
public union DaemonReport(ProcessExited, NookUsage);
