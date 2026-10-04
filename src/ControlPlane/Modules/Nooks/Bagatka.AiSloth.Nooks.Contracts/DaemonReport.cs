namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Something a nook's daemon reports on its control connection: a process exited, or how full the
/// disk is (on connecting, every 30 seconds, and whenever the disk fills up).
/// </summary>
public union DaemonReport(ProcessExited, DiskUsage);
