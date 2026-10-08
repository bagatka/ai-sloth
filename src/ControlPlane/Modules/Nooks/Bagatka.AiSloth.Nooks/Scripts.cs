using System;
using System.IO;
using System.Text;

namespace Bagatka.AiSloth.Nooks;

// The shell scripts the control plane runs in nooks, kept as files in Scripts/ (checked by
// shellcheck) and embedded in this assembly, so every nook runs the control plane's own version.
// Each file says what it takes and what it writes.
internal static class Scripts
{
    public static readonly string Archive = Read("archive.sh");
    public static readonly string Clone = Read("clone.sh");
    public static readonly string ConfigureGit = Read("configure-git.sh");
    public static readonly string CopyIn = Read("copy-in.sh");
    public static readonly string CopyOut = Read("copy-out.sh");
    public static readonly string Export = Read("export.sh");
    public static readonly string FindSetup = Read("find-setup.sh");
    public static readonly string Restore = Read("restore.sh");
    public static readonly string RunSetup = Read("run-setup.sh");
    public static readonly string Snapshot = Read("snapshot.sh");
    public static readonly string SyncFolder = Read("sync-folder.sh");

    private static string Read(string name)
    {
        using Stream? stream = typeof(Scripts).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            throw new InvalidOperationException("The script " + name + " isn't embedded in the Nooks module.");
        }

        using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
