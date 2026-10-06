using System;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Nooks;

// The module's log messages. Nooks appear by ID only.
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Nook {NookId} failed: {Reason}")]
    public static partial void NookFailed(ILogger logger, Guid nookId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nook {NookId} is unreachable: its daemon is away, and its provider can't say why; it waits for either")]
    public static partial void NookUnreachable(ILogger logger, Exception exception, Guid nookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nook {NookId} lost its sandbox; a new one starts from its latest checkpoint")]
    public static partial void NookReplaced(ILogger logger, Guid nookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted nook {NookId}")]
    public static partial void NookDeleted(ILogger logger, Guid nookId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconciling nook {NookId} failed; the next pass retries")]
    public static partial void ReconcilingFailed(ILogger logger, Exception exception, Guid nookId);

    [LoggerMessage(Level = LogLevel.Error, Message = "A reconciler pass failed; the next pass retries")]
    public static partial void PassFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nook {NookId} fell asleep")]
    public static partial void NookAsleep(ILogger logger, Guid nookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nook {NookId} slept long enough that its sandbox was deleted; it comes back from its latest checkpoint")]
    public static partial void NookEvicted(ILogger logger, Guid nookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nook {NookId} stays awake for now: {Reason}")]
    public static partial void NotAsleep(ILogger logger, Guid nookId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Putting nook {NookId} to sleep, or evicting it, failed; the next pass tries again")]
    public static partial void SleepingFailed(ILogger logger, Exception exception, Guid nookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Waking nook {NookId} failed; its next use tries again")]
    public static partial void WakingFailed(ILogger logger, Exception exception, Guid nookId);

    [LoggerMessage(Level = LogLevel.Error, Message = "A pass putting nooks to sleep failed; the next tries again")]
    public static partial void SleepPassFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nook {NookId}'s setup left a ready copy")]
    public static partial void ReadyCopyTaken(ILogger logger, Guid nookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nook {NookId}'s setup left no ready copy, so nooks with its files keep setting up from scratch")]
    public static partial void ReadyCopyFailed(ILogger logger, Exception exception, Guid nookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nook {NookId}'s ready copy is gone at its provider; it starts from its image")]
    public static partial void ReadyCopyGone(ILogger logger, Guid nookId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Deleting unused ready copies at provider {Provider} failed; the next hour tries again")]
    public static partial void PruningFailed(ILogger logger, Exception exception, string provider);
}
