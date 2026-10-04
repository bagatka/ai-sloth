using System.Runtime.InteropServices;

namespace Bagatka.AiSloth.Daemon;

/// <summary>
/// Sends signals to processes; .NET can kill a process but not ask it to stop.
/// </summary>
internal static partial class PosixSignals
{
    private const int SignalTerminate = 15;

    /// <summary>Asks a process to stop (SIGTERM). A process that already exited is ignored.</summary>
    public static void Terminate(int processId)
    {
        _ = Kill(processId, SignalTerminate);
    }

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Kill(int processId, int signal);
}
