using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// The agents' side of chats, called only by the WebApi's model gateway. It is never a public route
/// or a tool. An agent proves itself with its chat's token, so the actor is always
/// <see cref="Actor.Anonymous"/>.
/// </summary>
public interface IChatHarnessesApi
{
    /// <summary>
    /// The model provider's key to forward a call with, when the token belongs to a chat's running
    /// agent: its agent account's secret. Unauthorized otherwise, or when the account was removed.
    /// Never log or return the key.
    /// </summary>
    public Task<Result<string>> GetModelKeyAsync(Actor actor, string token, CancellationToken ct);
}
