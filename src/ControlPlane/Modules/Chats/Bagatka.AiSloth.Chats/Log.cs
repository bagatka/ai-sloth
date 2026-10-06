using System;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Chats;

// The module's log messages. Chats appear by ID only; what people and agents write never reaches the log.
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Error, Message = "The runner of chat {ChatId} failed; it retries in a few seconds")]
    public static partial void RunnerFailed(ILogger logger, Exception exception, Guid chatId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A turn of chat {ChatId} failed: {Reason}")]
    public static partial void AgentFailed(ILogger logger, Guid chatId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't sync the harness state of chat {ChatId}; its agent works with its nook's, and the next sync tries again: {Reason}")]
    public static partial void HarnessStateNotSynced(ILogger logger, Guid chatId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't write to the agent of chat {ChatId}: {Reason}")]
    public static partial void InputFailed(ILogger logger, Guid chatId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't delete nook {NookId}, which tested a setup; it stays in its workspace: {Reason}")]
    public static partial void TestNookKept(ILogger logger, Guid nookId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Chat {ChatId} went with its nook, nobody having written in it")]
    public static partial void DraftDropped(ILogger logger, Guid chatId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't delete nook {NookId} of a chat nobody wrote in; it stays in its workspace: {Reason}")]
    public static partial void DraftNookKept(ILogger logger, Guid nookId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "A pass deleting chats nobody wrote in failed; the next tries again")]
    public static partial void DraftsFailed(ILogger logger, Exception exception);
}
