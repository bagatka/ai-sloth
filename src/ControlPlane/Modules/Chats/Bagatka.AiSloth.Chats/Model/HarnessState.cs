using System;
using System.Globalization;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Model;

// A person's state for one harness in one workspace: the files of its state paths
// (HarnessProfile.StatePaths) their chats there merged in last (StateFiles), archived in object
// storage under its version, the SHA-256 of its manifest. A newer version replaces the row's, then the
// older archive goes.
internal sealed class HarnessState
{
    // Used by Save and by EF: parameter names match property names.
    private HarnessState(UserId personId, WorkspaceId workspaceId, string harness, DateTimeOffset savedAt, long bytes, byte[] sha256, ChatId savedFrom)
    {
        PersonId = personId;
        WorkspaceId = workspaceId;
        Harness = harness;
        SavedAt = savedAt;
        Bytes = bytes;
        Sha256 = sha256;
        SavedFrom = savedFrom;
    }

    public UserId PersonId { get; private set; }

    public WorkspaceId WorkspaceId { get; private set; }

    public string Harness { get; private set; }

    public DateTimeOffset SavedAt { get; private set; }

    public long Bytes { get; private set; }

    public byte[] Sha256 { get; private set; }

    public ChatId SavedFrom { get; private set; }

    // PostgreSQL's xmin: chats saving at once conflict, and the later merges again.
    public uint Version { get; private set; }

    // Its archive, alone in its version's folder, which goes with it.
    public string ObjectKey => KeyOf(PersonId, WorkspaceId, Harness, Sha256);

    public string VersionFolder => VersionFolderOf(PersonId, WorkspaceId, Harness, Sha256);

    // Every version's.
    public string Folder => FolderOf(PersonId, WorkspaceId, Harness);

    public static HarnessState Save(UserId personId, WorkspaceId workspaceId, string harness, long bytes, byte[] sha256, ChatId savedFrom, TimeProvider time)
    {
        return new HarnessState(personId, workspaceId, harness, time.GetUtcNow(), bytes, sha256, savedFrom);
    }

    public static string KeyOf(UserId personId, WorkspaceId workspaceId, string harness, byte[] version)
    {
        return VersionFolderOf(personId, workspaceId, harness, version) + "/state.tar.gz";
    }

    private static string VersionFolderOf(UserId personId, WorkspaceId workspaceId, string harness, byte[] version)
    {
        return FolderOf(personId, workspaceId, harness) + "/" + Convert.ToHexStringLower(version);
    }

    // Harness IDs are lowercase letters, digits, and dashes, as object keys take them.
    public static string FolderOf(UserId personId, WorkspaceId workspaceId, string harness)
    {
        return "people/" + personId.Value.ToString("D", CultureInfo.InvariantCulture)
            + "/workspaces/" + workspaceId.Value.ToString("D", CultureInfo.InvariantCulture)
            + "/harness-state/" + harness;
    }

    public void Saved(long bytes, byte[] sha256, ChatId savedFrom, TimeProvider time)
    {
        SavedAt = time.GetUtcNow();
        Bytes = bytes;
        Sha256 = sha256;
        SavedFrom = savedFrom;
    }

    public HarnessStateSummary ToSummary()
    {
        return new HarnessStateSummary(Harness, SavedAt, Bytes, SavedFrom);
    }
}
