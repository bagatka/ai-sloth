using System.Collections.Generic;
using System.Globalization;
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

// The instructions every agent gets, whatever its harness: AiSloth's about its nook, with how the
// project's setup ended when it failed, then its workspace's, then those of the person who started
// its chat. They are written, before the agent starts, to the file its harness reads its user's own
// instructions from (HarnessProfile.InstructionsPath), outside /work, so they never meet a
// repository's files and every session of the harness reads them.
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

        string text = Compose(Nook(chat.SetupExitCode), workspace ?? string.Empty, personal ?? string.Empty);
        using MemoryStream archive = await ArchiveAsync(harness.InstructionsPath, text, ct);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INooksApi>().CopyFilesInAsync(SystemActors.Harness, new CopyFilesIn(chat.NookId, Replacing: []), archive, ct);
    }

    // What every agent knows about its nook: what it has, how the project's setup works, and how the
    // setup ended when it failed.
    private static string Nook(int? setupExitCode)
    {
        string nook = "## Your nook\n\nYou work in a nook: a Linux machine of your own, with Docker. The project is in /work. "
            + "Its `.agents/setup` scripts, in /work or a folder directly in it, install what it needs, and its `.agents/resume` "
            + "scripts start its services; AiSloth runs them whenever a nook gets the project's files, before its agent starts. "
            + "When you add a tool, a dependency, or a service the project needs, update them, and keep them safe to run again.";
        if (setupExitCode is not int exitCode || exitCode == 0)
        {
            return nook;
        }

        return nook + string.Create(CultureInfo.InvariantCulture, $"\n\nBefore you started, the project's setup failed with exit code {exitCode}; ")
            + "its output is in /var/log/aisloth/setup.log. Fix the cause where you can, in the scripts too. "
            + "When people must do something, such as add a secret with `sloth secret set <NAME>`, tell them.";
    }

    private static string Compose(string nook, string workspace, string personal)
    {
        List<string> parts = [nook];
        if (workspace.Length > 0 || personal.Length > 0)
        {
            parts.Add("The people you work for gave the rest through AiSloth. Follow it everywhere, together with each repository's own instructions.");
        }

        if (workspace.Length > 0)
        {
            parts.Add("## From the workspace\n\n" + workspace.Trim());
        }

        if (personal.Length > 0)
        {
            parts.Add("## From the person who started this chat\n\n" + personal.Trim());
        }

        return "# Instructions from AiSloth\n\n" + string.Join("\n\n", parts) + "\n";
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
