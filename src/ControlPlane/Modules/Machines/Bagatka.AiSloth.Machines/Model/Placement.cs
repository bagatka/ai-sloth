using System;
using Bagatka.AiSloth.Machines.Contracts;

namespace Bagatka.AiSloth.Machines.Model;

// Which machine a sandbox or snapshot lives on, so a call by key goes to the right machine. Sandbox
// and snapshot keys are UUIDs, so one table holds both.
internal sealed class Placement(Guid key, MachineId machineId)
{
    public Guid Key { get; private set; } = key;

    public MachineId MachineId { get; private set; } = machineId;
}
