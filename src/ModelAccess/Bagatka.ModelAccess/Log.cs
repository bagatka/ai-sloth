using System;
using Microsoft.Extensions.Logging;

namespace Bagatka.ModelAccess;

// The library's log messages. Credentials never reach the log.
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading models.dev's catalog of models failed; the one at hand stays, and the next read is in an hour")]
    public static partial void CatalogNotRead(ILogger logger, Exception exception);
}
