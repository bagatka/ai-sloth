using System;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.AgentAccounts;

// The module's log messages. Accounts appear by ID only; secrets and tokens never reach the log.
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "The vendor ended the sign-in of agent account {AgentAccountId}: {Reason}")]
    public static partial void SignInEnded(ILogger logger, Guid agentAccountId, string reason);
}
