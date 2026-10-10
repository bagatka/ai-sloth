using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Chats.Harness;

// Wakes a chat's watchers when its runner saved new events, and a workspace's list watchers when
// what lists show of one of its chats changed. In memory: runners run on the active instance and
// wake its watchers. An instance that becomes active wakes all of its own, to read what the one
// before saved; an instance that hands over ends its watches (ActiveInstance.Leaving), and their
// clients resume on the next. An entry lives until its chat's or workspace's next change.
internal sealed class ChatSignals
{
    private readonly Lock _gate = new Lock();

    // Keyed by ChatId or WorkspaceId: the two never compare equal.
    private readonly Dictionary<object, TaskCompletionSource> _waiting = [];

    // Completes at the chat's next event. Take it before reading, so no event slips between.
    public Task NextAsync(ChatId chat)
    {
        return Next(chat);
    }

    // Completes at the next change of what lists show of the workspace's chats. Take it before reading.
    public Task NextAsync(WorkspaceId workspace)
    {
        return Next(workspace);
    }

    // Wakes every watcher, to read what it may have missed.
    public void NotifyAll()
    {
        List<TaskCompletionSource> waiting;
        lock (_gate)
        {
            waiting = [.. _waiting.Values];
            _waiting.Clear();
        }

        foreach (TaskCompletionSource next in waiting)
        {
            next.TrySetResult();
        }
    }

    public void Notify(ChatId chat)
    {
        Wake(chat);
    }

    public void Notify(WorkspaceId workspace)
    {
        Wake(workspace);
    }

    private Task Next(object key)
    {
        lock (_gate)
        {
            TaskCompletionSource? next = _waiting.GetValueOrDefault(key);
            if (next is null)
            {
                next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiting[key] = next;
            }

            return next.Task;
        }
    }

    private void Wake(object key)
    {
        TaskCompletionSource? next;
        lock (_gate)
        {
            _waiting.Remove(key, out next);
        }

        next?.TrySetResult();
    }
}
