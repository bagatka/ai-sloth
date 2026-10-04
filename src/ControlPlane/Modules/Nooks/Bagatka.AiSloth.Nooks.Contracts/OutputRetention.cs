namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// How a process's output is kept. Files are never affected: this is only what the process prints.
/// </summary>
public enum OutputRetention
{
    /// <summary>
    /// The most recent output is kept, at least the last 16 MiB; older output may be dropped, like a
    /// terminal's scrollback. For commands, terminals, and servers.
    /// </summary>
    Recent = 1,

    /// <summary>
    /// Nothing is dropped. Output waits on disk until it has been delivered to a watcher, and the last
    /// 16 MiB of delivered output is kept too, so a watcher that restarts can resume from what it
    /// saved. When 64 MiB is waiting because nothing reads it, the process blocks on its next write
    /// until something does. For processes whose every byte matters, such as an agent's conversation.
    /// </summary>
    Complete = 2,
}
