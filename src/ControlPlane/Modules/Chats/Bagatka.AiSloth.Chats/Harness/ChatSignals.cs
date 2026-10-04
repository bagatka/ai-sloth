using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;

namespace Bagatka.AiSloth.Chats.Harness;

// Wakes a chat's watchers when its runner saved new events. In memory: one active control-plane
// instance for now. An entry lives until the chat's next event.
internal sealed class ChatSignals
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<ChatId, TaskCompletionSource> _waiting = [];

    // Completes at the chat's next event. Take it before reading, so no event slips between.
    public Task NextAsync(ChatId chat)
    {
        lock (_gate)
        {
            if (!_waiting.TryGetValue(chat, out TaskCompletionSource? next))
            {
                next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiting[chat] = next;
            }

            return next.Task;
        }
    }

    public void Notify(ChatId chat)
    {
        TaskCompletionSource? next;
        lock (_gate)
        {
            _waiting.Remove(chat, out next);
        }

        next?.TrySetResult();
    }
}
