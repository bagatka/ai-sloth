using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

/// <summary>
/// What a machine keeps from registration. The token is a secret: the file is readable by its owner
/// only, and the token never reaches the log.
/// </summary>
internal sealed record MachineCredential(Uri ControlPlaneUrl, Guid MachineId, string Token)
{
    // Null when this computer hasn't been connected yet.
    public static async Task<MachineCredential?> ReadAsync(string path, CancellationToken ct)
    {
        return await PrivateFile.ReadAsync(path, CliJsonContext.Default.MachineCredential, ct);
    }

    public async Task WriteAsync(string path, CancellationToken ct)
    {
        await PrivateFile.WriteAsync(path, this, CliJsonContext.Default.MachineCredential, ct);
    }
}
