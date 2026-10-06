using System;
using Microsoft.Extensions.Logging;

namespace Bagatka.Foundation.Modules.Events;

// Event delivery's log messages. Events appear by ID and type, never by content.
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Reaction {Reaction} to event {EventId} failed: {Reason}; it is delivered again later")]
    public static partial void ReactionFailed(ILogger logger, Exception? exception, string reaction, Guid eventId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Event {EventId} of type {EventType} failed delivery {Attempts} times and is parked; an operator has to look at it")]
    public static partial void EventParked(ILogger logger, Guid eventId, string eventType, int attempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Delivering the events in the outbox of {Outbox} failed; the next pass tries again")]
    public static partial void DispatchFailed(ILogger logger, Exception exception, string outbox);
}
