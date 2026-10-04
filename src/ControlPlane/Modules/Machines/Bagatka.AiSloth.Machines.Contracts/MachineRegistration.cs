using System;

namespace Bagatka.AiSloth.Machines.Contracts;

/// <summary>
/// A machine just added, with the one-time code that registers it:
/// <c>sloth machine connect &lt;control plane&gt; &lt;code&gt;</c> on the machine.
/// </summary>
/// <param name="Machine">The machine, awaiting registration.</param>
/// <param name="Code">Registers the machine once; never shown again.</param>
/// <param name="CodeExpiresAt">When the code stops working.</param>
public sealed record MachineRegistration(MachineSummary Machine, string Code, DateTimeOffset CodeExpiresAt);
