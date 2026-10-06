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
/// instead of the test output: a run prints its tests' results, and a failing run's files explain it.
/// CI keeps them when tests fail.
/// </summary>
internal sealed class ResourceLogFiles : ILoggerProvider
{
    /// <summary>The category Aspire forwards each resource's output under, with the resource's name after it.</summary>
    public const string Category = "Bagatka.AiSloth.AppHost.Resources";

    private readonly string _directory;
    private readonly ConcurrentDictionary<string, ResourceLogFile> _files = new ConcurrentDictionary<string, ResourceLogFile>(StringComparer.Ordinal);

    /// <summary>Writes into the directory, which is made if need be.</summary>
    public ResourceLogFiles(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        if (!categoryName.StartsWith(Category + ".", StringComparison.Ordinal))
        {
            return NullLogger.Instance;
        }

        return _files.GetOrAdd(categoryName, name => new ResourceLogFile(Path.Combine(_directory, name[(Category.Length + 1)..] + ".log")));
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
