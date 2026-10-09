using System;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.WebApi;

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "PostHog lost {Events} events: {Reason}")]
    public static partial void PostHogEventsLost(ILogger logger, Exception? exception, int events, string reason);
}
