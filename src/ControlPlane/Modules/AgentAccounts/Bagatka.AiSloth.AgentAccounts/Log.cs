using System;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.AgentAccounts;

// The module's log messages. Accounts appear by ID only; secrets and tokens never reach the log.
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Removed agent account {AgentAccountId} without ending its sign-in at the vendor, which couldn't be reached")]
    public static partial void SignInNotEnded(ILogger logger, Exception exception, Guid agentAccountId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The vendor ended the sign-in of agent account {AgentAccountId}: {Reason}")]
    public static partial void SignInEnded(ILogger logger, Guid agentAccountId, string reason);
}
