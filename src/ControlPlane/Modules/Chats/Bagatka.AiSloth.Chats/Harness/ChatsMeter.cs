using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace Bagatka.AiSloth.Chats.Harness;

// What Chats measures: how long people wait from sending a message to the first thing its agent does
// for it, thinking, answering, or using a tool, with any nook, setup, and agent start before it
// (aisloth.chats.first_action, in seconds, by harness).
internal sealed class ChatsMeter : IDisposable
{
    private readonly Meter _meter;
    private readonly Histogram<double> _firstAction;

    public ChatsMeter(IMeterFactory meters)
    {
        _meter = meters.Create("Bagatka.AiSloth.Chats");
        _firstAction = _meter.CreateHistogram<double>(
            "aisloth.chats.first_action",
            unit: "s",
            description: "From a message sent to the first thing its agent does for it");
    }

    public void FirstAction(string harness, TimeSpan waited)
    {
        _firstAction.Record(waited.TotalSeconds, new KeyValuePair<string, object?>("harness", harness));
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
