using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Harnesses;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Chats.Harness;

// What harnesses write for themselves to use later (HarnessProfile.StatePaths), such as Claude Code's
// memory, kept as a folder AiSloth keeps (INooksApi.SyncFolderAsync) for whoever may direct the agents
// that write it: a person for the chats on their own accounts, and the workspace for the chats on its
// accounts. So nobody's notes reach agents they couldn't direct themselves. A chat syncs when its agent
// starts and after each turn. It never leaves its workspace, whose work it may describe. Failures are
// logged, never the chat's: the agent works with what its nook has, and the next sync tries again.
internal sealed class HarnessStates(INooksApi nooks, ILogger<HarnessStates> logger)
{
    public async Task SyncAsync(Chat chat, CancellationToken ct)
    {
        HarnessProfile? harness = HarnessProfiles.Find(chat.Harness);
        foreach (string path in harness?.StatePaths ?? [])
        {
            SyncFolder sync = new SyncFolder(chat.NookId, path, FolderOf(chat.WorkspaceId, chat.AccountOwnerId, chat.Harness, path));
            Result synced = await nooks.SyncFolderAsync(SystemActors.Harness, sync, ct);
            if (synced.Failed)
            {
                Log.HarnessStateNotSynced(logger, chat.Id.Value, synced.Error.Message);
            }
        }
    }

    // The folders of a person in the workspace, or the workspace's own for none; or those of one harness.
    public static string PrefixOf(WorkspaceId workspaceId, UserId? person, string? harness)
    {
        string keptFor = person is UserId someone ? someone.Value.ToString("N", CultureInfo.InvariantCulture) : "workspace";
        string prefix = "harness-state/" + workspaceId.Value.ToString("N", CultureInfo.InvariantCulture) + "/" + keptFor + "/";
        return harness is null ? prefix : prefix + harness + "/";
    }

    // The harness a folder of PrefixOf belongs to.
    public static string HarnessOf(string folder, string prefix)
    {
        return folder[prefix.Length..].Split('/')[0];
    }

    private static string FolderOf(WorkspaceId workspaceId, UserId? person, string harness, string path)
    {
        return PrefixOf(workspaceId, person, harness) + path.Trim('/');
    }
}
