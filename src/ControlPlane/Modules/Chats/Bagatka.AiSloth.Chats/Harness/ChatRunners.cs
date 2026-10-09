using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Chats.Harness;

// Owns the runners of the chats that have work, at most one per chat, on the active instance only
// (ActiveInstance): a runner starts when its chat gets a message or a stop there, and exits when the
// chat is idle. When this instance becomes active, chats that had work when the last one handed over
// get their runners back, among them those written in meanwhile on this one.
internal sealed class ChatRunners(
    IDbContextFactory<ChatsDbContext> databases,
    INooksApi nooks,
    IAgentAccountsApi accounts,
    AgentProcess agent,
    HarnessStates states,
    AgentInstructions instructions,
    NookSetups setups,
    ChatsMeter meter,
    ChatsSettings settings,
    ChatSignals signals,
    ActiveInstance active,
    IProductEvents productEvents,
    TimeProvider time,
    ILogger<ChatRunners> logger) : BackgroundService
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<ChatId, (ChatRunner Runner, Task Running)> _runners = [];
    private readonly CancellationTokenSource _stopping = new CancellationTokenSource();

    // Whether runners may start: from when this instance became active until it hands over.
    private bool _working;

    public void Wake(ChatId chat)
    {
        Runner(chat)?.Wake();
    }

    public void Stop(ChatId chat)
    {
        Runner(chat)?.Stop();
    }

    public override void Dispose()
    {
        _stopping.Dispose();
        base.Dispose();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return active.RunAsync(WorkAsync, stoppingToken);
    }

    private async Task WorkAsync(CancellationToken stoppingToken)
    {
        lock (_gate)
        {
            _working = true;
        }

        List<ChatId> busy;
        await using (ChatsDbContext db = await databases.CreateDbContextAsync(stoppingToken))
        {
            busy = await db.Chats
                .Where(chat => chat.TurnMessageId != null || (chat.HarnessProcessId != null && chat.SessionId == null) || chat.StartsAgent
                    || db.Messages.Any(message => message.ChatId == chat.Id
                        && (message.State == MessageState.New || message.State == MessageState.Queued || message.State == MessageState.Steering)))
                .Select(chat => chat.Id)
                .ToListAsync(stoppingToken);
        }

        foreach (ChatId chat in busy)
        {
            Wake(chat);
        }

        // Watchers that came while another instance was active heard nothing of its events.
        signals.NotifyAll();

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, time, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: runners stop, and the agents keep working in their nooks.
        }

        Task[] running;
        lock (_gate)
        {
            _working = false;
            running = [.. _runners.Values.Select(entry => entry.Running)];
        }

        await _stopping.CancelAsync();

        await Task.WhenAll(running);
    }

    // The chat's runner, started now if it has none; null on an instance that isn't active.
    private ChatRunner? Runner(ChatId chat)
    {
        lock (_gate)
        {
            if (!_working)
            {
                return null;
            }

            bool hasRunner = _runners.ContainsKey(chat);
            if (hasRunner)
            {
                return _runners[chat].Runner;
            }

            ChatRunner runner = new ChatRunner(chat, databases, nooks, accounts, agent, states, instructions, setups, meter, settings, signals, productEvents, time, logger);
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
