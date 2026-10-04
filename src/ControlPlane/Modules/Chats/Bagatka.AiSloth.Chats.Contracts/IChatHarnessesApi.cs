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
    /// Succeeds when the token belongs to a chat's running agent, whose model calls the gateway then
    /// forwards; unauthorized otherwise.
    /// </summary>
    public Task<Result> AuthorizeModelCallAsync(Actor actor, string token, CancellationToken ct);
}
