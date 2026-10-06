using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// What an app's resources print, the WebApi's logs among them, written to one file per resource
/// instead of the test output, and Aspire's own warnings to aspire.log: a run prints its tests'
/// results, and a failing run's files explain it. CI keeps them when tests fail.
/// </summary>
internal sealed class ResourceLogFiles : ILoggerProvider
{
    /// <summary>The category Aspire forwards each resource's output under, with the resource's name after it.</summary>
    public const string Category = "Bagatka.AiSloth.AppHost.Resources";

    /// <summary>The categories of Aspire's own logs.</summary>
    public const string AspireCategory = "Aspire.Hosting";

    private readonly string _directory;
    private readonly ConcurrentDictionary<string, ResourceLogFile> _files = new ConcurrentDictionary<string, ResourceLogFile>(StringComparer.Ordinal);

    /// <summary>Writes into the directory, which is made if need be.</summary>
    public ResourceLogFiles(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    /// <summary>
    /// Sends the app's output and Aspire's warnings here instead of the test output, and leaves out this
    /// process's routine logs: its HTTP calls, and health checks, which fail as every run stops its
    /// containers. Aspire's warnings include a watch of a kind of resource this app never makes timing
    /// out, and a missing development certificate, which only HTTPS endpoints need.
    /// </summary>
    public void Route(ILoggingBuilder logging)
    {
        ArgumentNullException.ThrowIfNull(logging);
        logging
            .AddFilter(Category, LogLevel.None)
            .AddFilter<ResourceLogFiles>(Category, LogLevel.Trace)
            .AddFilter(AspireCategory, LogLevel.None)
            .AddFilter<ResourceLogFiles>(AspireCategory, LogLevel.Warning)

            // The AppHost's settings give Aspire's orchestrator a level of its own, which a rule for
            // its own category outranks.
            .AddFilter(AspireCategory + ".Dcp", LogLevel.None)
            .AddFilter<ResourceLogFiles>(AspireCategory + ".Dcp", LogLevel.Warning)
            .AddFilter("System.Net.Http.HttpClient", LogLevel.Warning)
            .AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks", LogLevel.None)
            .AddProvider(this);
    }

    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        if (categoryName.StartsWith(Category + ".", StringComparison.Ordinal))
        {
            return _files.GetOrAdd(categoryName[(Category.Length + 1)..], FileFor);
        }

        if (categoryName.StartsWith(AspireCategory, StringComparison.Ordinal))
        {
            return _files.GetOrAdd("aspire", FileFor);
        }

        return NullLogger.Instance;
    }

    private ResourceLogFile FileFor(string name)
    {
        return new ResourceLogFile(Path.Combine(_directory, name + ".log"));
    }

    public void Dispose()
    {
        foreach (ResourceLogFile file in _files.Values)
        {
            file.Dispose();
        }
    }

    // One resource's lines, as it printed them; written as they come, so a crashed run keeps them. The
    // file is made with the first line, so resources that print nothing, such as parameters, have none.
    private sealed class ResourceLogFile(string path) : ILogger, IDisposable
    {
        private readonly Lock _writing = new Lock();
        private StreamWriter? _writer;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            string line = formatter(state, exception);
            lock (_writing)
            {
                _writer ??= new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
                _writer.WriteLine(line);
                if (exception is not null)
                {
                    _writer.WriteLine(exception.ToString());
                }
            }
        }

        public void Dispose()
        {
            lock (_writing)
            {
                _writer?.Dispose();
            }
        }
    }
}
