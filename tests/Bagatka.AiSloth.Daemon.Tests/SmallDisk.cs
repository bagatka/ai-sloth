using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;

namespace Bagatka.AiSloth.Daemon.Tests;

/// <summary>
/// A tiny tmpfs, so a test can really fill a disk. Mounting needs root, so tests that use it skip
/// elsewhere, such as on CI runners without root.
/// </summary>
internal sealed class SmallDisk : IAsyncDisposable
{
    private SmallDisk(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static async Task<SmallDisk> MountAsync(int mebibytes)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "slothd-disks", RandomNumberGenerator.GetHexString(12, lowercase: true));
        Directory.CreateDirectory(path);
        string size = string.Create(CultureInfo.InvariantCulture, $"size={mebibytes}m");
        if (await RunAsync("mount", "-t", "tmpfs", "-o", size, "tmpfs", path) != 0)
        {
            Directory.Delete(path);
            Assert.Skip("Mounting a tmpfs needs root.");
        }

        return new SmallDisk(path);
    }

    /// <summary>Writes a filler file until the disk is full.</summary>
    public async Task FillAsync()
    {
        byte[] block = new byte[64 * 1024];
        await using FileStream filler = new FileStream(System.IO.Path.Combine(Path, "filler"), FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 0);
        try
        {
            while (true)
            {
                await filler.WriteAsync(block);
            }
        }
        catch (IOException)
        {
            // Full.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await RunAsync("umount", Path);
        Directory.Delete(Path, recursive: true);
    }

    private static async Task<int> RunAsync(string command, params string[] arguments)
    {
        using Process process = Process.Start(new ProcessStartInfo(command, arguments) { UseShellExecute = false, RedirectStandardError = true })
            ?? throw new InvalidOperationException(command + " didn't start.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
