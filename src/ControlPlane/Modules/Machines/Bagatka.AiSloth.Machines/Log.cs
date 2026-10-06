using System;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Machines;

// The module's log messages. Machines appear by ID only.
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Machine {MachineId} stopped answering, so its connection ended; it is offline until it dials again")]
    public static partial void MachineSilent(ILogger logger, Guid machineId);
}
