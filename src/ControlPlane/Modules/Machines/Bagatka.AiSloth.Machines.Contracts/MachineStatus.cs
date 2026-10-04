namespace Bagatka.AiSloth.Machines.Contracts;

/// <summary>
/// Whether a machine can run nooks right now.
/// </summary>
public enum MachineStatus
{
    /// <summary>Added, but its registration code hasn't been used yet.</summary>
    AwaitingRegistration = 1,

    /// <summary>Registered, and not connected now.</summary>
    Offline = 2,

    /// <summary>Connected: nooks can start on it.</summary>
    Online = 3,
}
