using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Nooks.Model;

// A copy of a nook right after a setup that took a while, which the next nooks with the same match
// start from: the same workspace, provider and place, image, and repositories. One per match; a newer
// one replaces it. Its files are a snapshot at the provider.
internal sealed class ReadyCopy
{
    public const int MatchLength = 64;

    // How long a setup runs before its result is worth keeping as a ready copy.
    public static readonly TimeSpan WorthKeeping = TimeSpan.FromSeconds(15);

    // How long a ready copy nobody starts from is kept.
    public static readonly TimeSpan KeptUnused = TimeSpan.FromDays(7);

    // Used by Make and by EF: parameter names match property names.
    private ReadyCopy(string match, WorkspaceId workspaceId, string provider, string? location, Guid snapshot, NookId madeFrom, DateTimeOffset madeAt, DateTimeOffset usedAt)
    {
        Match = match;
        WorkspaceId = workspaceId;
        Provider = provider;
        Location = location;
        Snapshot = snapshot;
        MadeFrom = madeFrom;
        MadeAt = madeAt;
        UsedAt = usedAt;
    }

    public string Match { get; private set; }

    public WorkspaceId WorkspaceId { get; private set; }

    public string Provider { get; private set; }

    public string? Location { get; private set; }

    // The provider's snapshot key.
    public Guid Snapshot { get; private set; }

    public NookId MadeFrom { get; private set; }

    public DateTimeOffset MadeAt { get; private set; }

    // When a nook last started from it.
    public DateTimeOffset UsedAt { get; private set; }

    // PostgreSQL's xmin: two nooks replacing one copy at once conflict.
    public uint Version { get; private set; }

    // What a nook must share with a ready copy to start from it, as SHA-256 hex.
    public static string MatchOf(Nook nook, string image, IEnumerable<SourceCopy> repositories)
    {
        StringBuilder canonical = new StringBuilder();
        canonical.Append(nook.WorkspaceId.Value.ToString("N", CultureInfo.InvariantCulture)).Append('\n');
        canonical.Append(nook.Provider).Append('\n').Append(nook.Location).Append('\n').Append(image);
        foreach (SourceCopy repository in repositories.OrderBy(repository => repository.Name, StringComparer.Ordinal))
        {
            canonical.Append('\n').Append(repository.RepositoryId.Value.ToString("N", CultureInfo.InvariantCulture)).Append(' ').Append(repository.Name);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    public static ReadyCopy Make(string match, Nook nook, Guid snapshot, TimeProvider time)
    {
        DateTimeOffset now = time.GetUtcNow();
        return new ReadyCopy(match, nook.WorkspaceId, nook.Provider, nook.Location, snapshot, nook.Id, now, now);
    }

    // A newer copy takes its place; returns the snapshot it replaces.
    public Guid Replace(Guid snapshot, NookId madeFrom, TimeProvider time)
    {
        Guid replaced = Snapshot;
        Snapshot = snapshot;
        MadeFrom = madeFrom;
        MadeAt = time.GetUtcNow();
        UsedAt = MadeAt;
        return replaced;
    }

    public void Used(TimeProvider time)
    {
        UsedAt = time.GetUtcNow();
    }
}
