using System.Text;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// An agent account's secret, for running an agent with it. Never log it, store it, or return it to
/// a client; its text form leaves the secret out.
/// </summary>
/// <param name="Id">The account.</param>
/// <param name="Kind">What it is at its vendor.</param>
/// <param name="OwnerId">The person it belongs to, for a personal account.</param>
/// <param name="Shareable">Whether its owner may let other people message chats running on it.</param>
/// <param name="Secret">The key or token.</param>
public sealed record AgentAccountCredential(AgentAccountId Id, AgentAccountKind Kind, UserId? OwnerId, bool Shareable, string Secret)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Id = ").Append(Id.Value).Append(", Kind = ").Append(Kind).Append(", Secret = ***");
        return true;
    }
}
