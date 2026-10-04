using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Chats.Harness;

// Owns the runners of the chats that have work, at most one per chat. A runner starts when its chat
// gets a message or a stop, and exits when the chat is idle. At startup, chats that had work when the
// last instance stopped get their runners back.
internal sealed class ChatRunners(
    IDbContextFactory<ChatsDbContext> databases,
    IServiceScopeFactory scopes,
    AgentProcess agent,
    ChatsSettings settings,
    ChatSignals signals,
    TimeProvider time,
    ILogger<ChatRunners> logger) : BackgroundService
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<ChatId, (ChatRunner Runner, Task Running)> _runners = [];
    private readonly CancellationTokenSource _stopping = new CancellationTokenSource();

    public void Wake(ChatId chat)
    {
        Runner(chat).Wake();
    }

    public void Stop(ChatId chat)
    {
        Runner(chat).Stop();
    }

    public override void Dispose()
    {
        _stopping.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<ChatId> busy;
        await using (ChatsDbContext db = await databases.CreateDbContextAsync(stoppingToken))
        {
            busy = await db.Chats
                .Where(chat => chat.TurnMessageId != null || (chat.HarnessProcessId != null && chat.SessionId == null)
                    || db.Messages.Any(message => message.ChatId == chat.Id
                        && (message.State == MessageState.New || message.State == MessageState.Queued || message.State == MessageState.Steering)))
                .Select(chat => chat.Id)
                .ToListAsync(stoppingToken);
        }

        foreach (ChatId chat in busy)
        {
            Wake(chat);
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, time, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: runners stop, and the agents keep working in their nooks.
        }

        await _stopping.CancelAsync();
        Task[] running;
        lock (_gate)
        {
            running = [.. _runners.Values.Select(entry => entry.Running)];
        }

        await Task.WhenAll(running);
    }

    private ChatRunner Runner(ChatId chat)
    {
        lock (_gate)
        {
            bool hasRunner = _runners.ContainsKey(chat);
            if (hasRunner)
            {
                return _runners[chat].Runner;
            }

            ChatRunner runner = new ChatRunner(chat, databases, scopes, agent, settings, signals, time, logger);
            Task running = Task.Run(() => runner.RunAsync(Retire, _stopping.Token), CancellationToken.None);
            _runners[chat] = (runner, running);
            return runner;
        }
    }

    // A runner leaves only when nothing waits for it; otherwise it goes on.
    private bool Retire(ChatRunner runner)
    {
        lock (_gate)
        {
            if (runner.HasInput)
            {
                return false;
            }

            _runners.Remove(runner.ChatId);
            return true;
        }
    }
}
