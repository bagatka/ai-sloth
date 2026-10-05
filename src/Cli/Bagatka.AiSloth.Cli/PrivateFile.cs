using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// A file only its owner may read, for what sloth keeps between runs: sessions and a machine's token.
// On Linux and macOS its mode says so; on Windows its folder is the user's own local application data.
// It is written beside the old file and moved over it, so a crash never leaves half of it.
internal static class PrivateFile
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    // Null when the file doesn't exist yet.
    public static async Task<T?> ReadAsync<T>(string path, JsonTypeInfo<T> type, CancellationToken ct)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream file = File.OpenRead(path);
        T? value = await JsonSerializer.DeserializeAsync(file, type, ct);
        if (value is null)
        {
            throw new InvalidDataException(path + " is empty.");
        }

        return value;
    }

    public static async Task WriteAsync<T>(string path, T value, JsonTypeInfo<T> type, CancellationToken ct)
    {
        string directory = Path.GetDirectoryName(path)!;
        FileStreamOptions options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, OwnerOnly | UnixFileMode.UserExecute);
            options.UnixCreateMode = OwnerOnly;
        }

        string written = path + ".new";
        await using (FileStream file = new FileStream(written, options))
        {
            await JsonSerializer.SerializeAsync(file, value, type, ct);
        }

        File.Move(written, path, overwrite: true);
    }
}
