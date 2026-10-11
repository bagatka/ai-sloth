using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.Foundation;
using Bagatka.Harnesses;
using Bagatka.ModelAccess;

namespace Bagatka.AiSloth.Chats.Harness;

// What one chat's work tells the people who run AiSloth, kept by the chat's runner: a trace of each
// turn, from when a message waits for the agent, its start included, to the turn's end, current only
// in the steps that work on the turn, so the runner's background reading and waiting stay out of it;
// how long the turn's message waited for the agent's first action; and a product event when a turn or
// a setup ends. Never the words of a message or a failure, which can quote people or a vendor.
internal sealed class TurnTelemetry(ChatId chatId, ChatsMeter meter, IProductEvents productEvents, TimeProvider time)
{
    private static readonly ActivitySource Traces = new ActivitySource("Bagatka.AiSloth.Chats");

    private Activity? _turn;
    private bool _turnEnded;

    // When the message of the running turn was sent, until its agent first did something for it; then
    // how long that took.
    private DateTimeOffset? _firstActionDue;
    private TimeSpan? _firstActionWaited;

    // The model that last answered, or else the one the agent's session started with, when the agent
    // said, and the kind of account it runs on, as its last turn found it. A session's model can be
    // "default", the agent's own choice, until a model answers.
    private string? _model;
    private ModelAccessKind? _accountKind;

    // The trace of the turn the chat works on, for the steps that work on it.
    public Activity? Turn => _turn;

    // The chat has work for its agent: a turn's trace starts, unless one runs.
    public void Working(Chat chat)
    {
        if (_turn is not null)
        {
            return;
        }

        _turn = Traces.StartActivity("turn");
        _turn?.SetTag("chat.id", chatId.Value.ToString("D", CultureInfo.InvariantCulture));
        _turn?.SetTag("harness", chat.Harness);
    }

    // The chat has nothing more for its agent: a turn's trace still open was stopped before the agent
    // took its message.
    public void Idle()
    {
        if (_turn is not null && !_turnEnded)
        {
            EndTrace("cancelled", failure: null);
        }
    }

    // Closes the trace of a turn that ended, once its end is saved.
    public void CloseEnded()
    {
        if (!_turnEnded)
        {
            return;
        }

        _turn?.Dispose();
        _turn = null;
        _turnEnded = false;
    }

    public void TurnStarted(DateTimeOffset sentAt, ModelAccessKind accountKind)
    {
        _firstActionDue = sentAt;
        _firstActionWaited = null;
        _accountKind = accountKind;
    }

    public void SessionReady(string? model)
    {
        _model = model;
    }

    // The first time the agent thinks, answers, or uses a tool in a turn, how long its message waited.
    public void Reported(Chat chat, AgentReported reported)
    {
        _model = reported.Model ?? _model;
        bool named = reported.Update.TryGetProperty("sessionUpdate", out JsonElement kind);
        bool acted = named && kind.GetString() is "agent_thought_chunk" or "agent_message_chunk" or "tool_call" or "plan";
        if (acted && chat.TurnMessageId is not null && _firstActionDue is DateTimeOffset sentAt)
        {
            _firstActionWaited = time.GetUtcNow() - sentAt;
            meter.FirstAction(chat.Harness, _firstActionWaited.Value);
            _firstActionDue = null;
        }
    }

    // What a turn came to: how it ended, and for a failed one where it failed (start: the agent
    // couldn't start; agent_error: it answered with an error, such as its model's; agent_exited: it
    // stopped); how long its message waited for that, and for the agent's first action, when this
    // runner saw it; the model, when the agent said; and whether the turn's files were saved, for a turn
    // that had any.
    public void TurnEnded(Chat chat, UserId sentBy, DateTimeOffset sentAt, TurnEnded ended, string? failure, bool? checkpointSaved)
    {
        Dictionary<string, ProductFact> facts = new Dictionary<string, ProductFact>(StringComparer.Ordinal)
        {
            ["harness"] = new ProductFact(chat.Harness),
            ["outcome"] = new ProductFact(ended.StopReason),
            ["seconds"] = new ProductFact((time.GetUtcNow() - sentAt).TotalSeconds),
        };
        if (failure is not null)
        {
            facts["failure"] = new ProductFact(failure);
        }

        if (_firstActionWaited is TimeSpan waited)
        {
            facts["first_action_seconds"] = new ProductFact(waited.TotalSeconds);
        }

        if (_model is not null)
        {
            facts["model"] = new ProductFact(_model);
        }

        if (_accountKind is ModelAccessKind kind)
        {
            facts["account_kind"] = new ProductFact(kind.ToString());
        }

        if (checkpointSaved is bool saved)
        {
            facts["checkpoint_saved"] = new ProductFact(saved);
        }

        productEvents.Capture(new ProductEvent("turn_ended", sentBy, chat.WorkspaceId.Value, facts));
        _firstActionWaited = null;
        EndTrace(ended.StopReason, failure);
    }

    // How a nook's setup went before its agent could start.
    public void SetupEnded(Chat chat, int exitCode, TimeSpan took, int scripts, bool fromReadyCopy)
    {
        productEvents.Capture(new ProductEvent("setup_ended", chat.StartedBy, chat.WorkspaceId.Value, new Dictionary<string, ProductFact>(StringComparer.Ordinal)
        {
            ["succeeded"] = new ProductFact(exitCode == 0),
            ["seconds"] = new ProductFact(took.TotalSeconds),
            ["scripts"] = new ProductFact(scripts),
            ["from_ready_copy"] = new ProductFact(fromReadyCopy),
        }));
    }

    // The runner stopped: the trace it had open ends with it.
    public void Stopped()
    {
        _turn?.Dispose();
        _turn = null;
    }

    // The turn's trace ends with how it ended, and for a failed one where it failed.
    private void EndTrace(string outcome, string? failure)
    {
        _turn?.SetTag("outcome", outcome);
        if (failure is not null)
        {
            _turn?.SetStatus(ActivityStatusCode.Error, failure);
        }

        _turnEnded = true;
    }
}
