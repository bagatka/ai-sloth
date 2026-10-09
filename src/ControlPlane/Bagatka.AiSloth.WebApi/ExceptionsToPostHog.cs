using System;
using System.Text.Json.Nodes;
using Bagatka.PostHog;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// Turns every exception the host logs at Error or worse into an error tracking issue in its PostHog
/// project, with the log's category and message: whatever the code already reports as a failure is
/// seen there before anyone reports it. The events belong to the host, not to a person.
/// </summary>
internal sealed class ExceptionsToPostHog(PostHogClient postHog) : ILoggerProvider
{
    private const string DistinctId = "webapi";

    public ILogger CreateLogger(string categoryName)
    {
        return new Logger(postHog, categoryName);
    }

    public void Dispose()
    {
    }

    private sealed class Logger(PostHogClient postHog, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Error;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is null || logLevel < LogLevel.Error)
            {
                return;
            }

            JsonObject properties = new JsonObject
            {
                ["$process_person_profile"] = false,
                ["category"] = category,
                ["message"] = formatter(state, exception),
                ["level"] = logLevel.ToString(),
            };
            postHog.CaptureException(exception, DistinctId, properties);
        }
    }
}
