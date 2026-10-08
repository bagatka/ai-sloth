using System;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Nooks;

// The module's log messages. Nooks appear by ID only.
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Nook {NookId} failed: {Reason}")]
    public static partial void NookFailed(ILogger logger, Guid nookId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nook {NookId} is offline: its daemon is away, and its provider can't be asked; it waits for either")]
    public static partial void NookOffline(ILogger logger, Exception exception, Guid nookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nook {NookId} lost its sandbox; a new one starts from its latest checkpoint")]
    public static partial void NookReplaced(ILogger logger, Guid nookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Work on nook {NookId} took longer than {Deadline} and stopped; a later pass tries again")]
    public static partial void NookWorkTooLong(ILogger logger, Guid nookId, TimeSpan deadline);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted nook {NookId}")]
    public static partial void NookDeleted(ILogger logger, Guid nookId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Work on nook {NookId} failed; the next pass tries again")]
    public static partial void NookWorkFailed(ILogger logger, Exception exception, Guid nookId);

    [LoggerMessage(Level = LogLevel.Error, Message = "A pass of the nooks' lifecycle failed; the next pass tries again")]
    public static partial void PassFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nook {NookId} fell asleep")]
    public static partial void NookAsleep(ILogger logger, Guid nookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nook {NookId} slept long enough that its sandbox was deleted; it comes back from its latest checkpoint")]
    public static partial void NookEvicted(ILogger logger, Guid nookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nook {NookId} stays awake for now: {Reason}")]
    public static partial void NotAsleep(ILogger logger, Guid nookId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nook {NookId} woke for {Actor}")]
    public static partial void NookWoke(ILogger logger, Guid nookId, string actor);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Waking nook {NookId} failed; its next use tries again")]
    public static partial void WakingFailed(ILogger logger, Exception exception, Guid nookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nook {NookId}'s setup left a ready copy of its files, {Match}")]
    public static partial void ReadyCopyTaken(ILogger logger, Guid nookId, string match);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nook {NookId} starts from the ready copy of its files, {Match}")]
    public static partial void ReadyCopyFound(ILogger logger, Guid nookId, string match);

    [LoggerMessage(Level = LogLevel.Information, Message = "No ready copy of nook {NookId}'s files, {Match}, so it sets up from scratch")]
    public static partial void ReadyCopyMissing(ILogger logger, Guid nookId, string match);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nook {NookId}'s setup left no ready copy, so nooks with its files keep setting up from scratch")]
    public static partial void ReadyCopyFailed(ILogger logger, Exception exception, Guid nookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nook {NookId}'s ready copy is gone at its provider; it starts from its image")]
    public static partial void ReadyCopyGone(ILogger logger, Guid nookId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting unused ready copies took longer than {Deadline} and stopped; the next hour tries again")]
    public static partial void PruningTooLong(ILogger logger, TimeSpan deadline);

    [LoggerMessage(Level = LogLevel.Error, Message = "Deleting unused ready copies failed; the next hour tries again")]
    public static partial void PruneFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Deleting what nobody uses at provider {Provider} failed; the next hour tries again")]
    public static partial void PruningFailed(ILogger logger, Exception exception, string provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleted sandbox {NookId} at provider {Provider}: no nook records it")]
    public static partial void OrphanDeleted(ILogger logger, Guid nookId, string provider);
}
