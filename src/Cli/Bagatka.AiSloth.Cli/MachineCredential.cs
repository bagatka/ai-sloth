using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

/// <summary>
/// What a machine keeps from registration. The token is a secret: the file is readable by its owner
/// only, and the token never reaches the log.
/// </summary>
internal sealed record MachineCredential(Uri ControlPlaneUrl, Guid MachineId, string Token)
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    // Null when this computer hasn't been connected yet.
    public static async Task<MachineCredential?> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream file = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(file, CliJsonContext.Default.MachineCredential, ct)
            ?? throw new InvalidDataException(path + " is empty.");
    }

    // Written beside the old file and moved over it, so a crash never leaves half a credential.
    public async Task WriteAsync(string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!, OwnerOnly | UnixFileMode.UserExecute);
        string written = path + ".new";
        FileStreamOptions options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = OwnerOnly };
        await using (FileStream file = new FileStream(written, options))
        {
            await JsonSerializer.SerializeAsync(file, this, CliJsonContext.Default.MachineCredential, ct);
        }

        File.Move(written, path, overwrite: true);
    }
}
