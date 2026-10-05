using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;
using Bagatka.Harnesses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.Chats.Harness;

// The instructions every agent gets, whatever its harness: its workspace's, then those of the person
// who started its chat. They are written, before the agent starts, to the file its harness reads its
// user's own instructions from (HarnessProfile.InstructionsPath), outside /work, so they never meet
// a repository's files and every session of the harness reads them.
internal sealed class AgentInstructions(IDbContextFactory<ChatsDbContext> databases, IServiceScopeFactory scopes)
{
    // Each text's limit: instructions go into every model call the agent makes.
    public const int MaxLength = 10_000;

    public static Error? Check(string text)
    {
        return text.Length > MaxLength ? Error.Validation("text", "At most 10,000 characters.") : null;
    }

    // Writes the chat's instructions into its nook, replacing the earlier ones; empty when there are none.
    public async Task<Result> WriteAsync(Chat chat, HarnessProfile harness, CancellationToken ct)
    {
        string? workspace;
        string? personal;
        await using (ChatsDbContext db = await databases.CreateDbContextAsync(ct))
        {
            workspace = await db.WorkspaceInstructions.Where(found => found.WorkspaceId == chat.WorkspaceId).Select(found => found.Text).SingleOrDefaultAsync(ct);
            personal = await db.PersonalInstructions.Where(found => found.PersonId == chat.StartedBy).Select(found => found.Text).SingleOrDefaultAsync(ct);
        }

        using MemoryStream archive = await ArchiveAsync(harness.InstructionsPath, Compose(workspace ?? string.Empty, personal ?? string.Empty), ct);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INooksApi>().CopyFilesInAsync(SystemActors.Harness, new CopyFilesIn(chat.NookId, Replacing: []), archive, ct);
    }

    private static string Compose(string workspace, string personal)
    {
        List<string> parts = [];
        if (workspace.Length > 0)
        {
            parts.Add("## From the workspace\n\n" + workspace.Trim());
        }

        if (personal.Length > 0)
        {
            parts.Add("## From the person who started this chat\n\n" + personal.Trim());
        }

        if (parts.Count == 0)
        {
            return string.Empty;
        }

        string introduction = "# Instructions from AiSloth\n\nThe people you work for gave these through AiSloth. Follow them everywhere, "
            + "together with each repository's own instructions.";
        return introduction + "\n\n" + string.Join("\n\n", parts) + "\n";
    }

    // A gzipped tar archive holding the one file, by its path relative to /, as Nooks unpacks it.
    private static async Task<MemoryStream> ArchiveAsync(string path, string text, CancellationToken ct)
    {
        MemoryStream archive = new MemoryStream();
        await using (GZipStream zipped = new GZipStream(archive, CompressionLevel.Fastest, leaveOpen: true))
        await using (TarWriter tar = new TarWriter(zipped, TarEntryFormat.Pax, leaveOpen: true))
        {
            using MemoryStream content = new MemoryStream(Encoding.UTF8.GetBytes(text));
            PaxTarEntry entry = new PaxTarEntry(TarEntryType.RegularFile, path.TrimStart('/'))
            {
                DataStream = content,
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
            };
            await tar.WriteEntryAsync(entry, ct);
        }

        archive.Position = 0;
        return archive;
    }
}
