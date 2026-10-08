using System.IO;

namespace Bagatka.AiSloth.Nooks;

// Where a script's standard output goes, and the most of it the control plane takes, whatever the
// nook prints: nooks are untrusted, so a check inside one bounds nothing here. A small answer is read
// into memory; a large one, such as a checkpoint or a download, goes to the caller's stream, usually a
// file on this instance's disk. NookProcesses also bounds how many of each run at once.
internal sealed record ScriptOutput(Stream Destination, long Limit, bool Large)
{
    // The most a checkpoint, download, or export may come to.
    public const long LargeLimit = 2L * 1024 * 1024 * 1024;

    public static ScriptOutput Small(MemoryStream destination, long limit)
    {
        return new ScriptOutput(destination, limit, Large: false);
    }

    public static ScriptOutput To(Stream destination)
    {
        return new ScriptOutput(destination, LargeLimit, Large: true);
    }
}
