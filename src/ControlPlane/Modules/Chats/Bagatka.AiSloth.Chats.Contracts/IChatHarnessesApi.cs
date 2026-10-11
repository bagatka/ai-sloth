using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
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
    /// Where to forward a call, and the headers that pay for it, when the token belongs to a chat's
    /// running agent: its agent account's endpoint (<see cref="IAgentAccountsApi.UseAsync"/>). Never log
    /// or return the headers.
    /// </summary>
    /// <returns>
    /// The endpoint; unauthorized for a token no chat's agent holds; or the account's error when it can't
    /// be used, such as not found after it was removed, or <see cref="AgentAccountsErrors.SignInEnded"/>.
    /// </returns>
    public Task<Result<ModelEndpoint>> GetModelEndpointAsync(Actor actor, string token, CancellationToken ct);

    /// <summary>
    /// Records a call the gateway forwarded for the agent holding the token, once its answer is in:
    /// its tokens, with its cost from the model's price at the chat's account (or the vendor's own
    /// figure), in the turn the chat was on when the call started. Persists one row.
    /// </summary>
    /// <param name="actor">Anonymous: the token is the credential.</param>
    /// <param name="token">The chat's token the call carried.</param>
    /// <param name="usage">What the answer reported.</param>
    /// <param name="startedAt">When the call reached the gateway.</param>
    /// <param name="ct">Cancels the recording.</param>
    /// <returns>Success; or unauthorized for a token no chat's agent holds.</returns>
    public Task<Result> RecordModelCallAsync(Actor actor, string token, ModelCallUsage usage, DateTimeOffset startedAt, CancellationToken ct);
}
