using System.Globalization;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>The public API's paths to what journeys work with.</summary>
internal static class Paths
{
    public static string Workspace(WorkspaceId workspace)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Value}");
    }

    public static string Chat(ChatSummary chat)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}");
    }

    public static string Account(AgentAccountId account)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/agent-accounts/{account.Value}");
    }

    public static string Nook(NookId nook)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/nooks/{nook.Value}");
    }
}
