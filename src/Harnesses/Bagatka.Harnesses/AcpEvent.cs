namespace Bagatka.Harnesses;

/// <summary>
/// What a line a harness wrote means to the client (<see cref="Acp.Read"/>): progress of the running
/// turn, a request to answer, or the answer to one of the client's requests, already matched to it.
/// </summary>
public union AcpEvent(AcpUpdate, AcpRequest, AcpInitialized, AcpSessionCreated, AcpSessionLoaded, AcpLoadFailed, AcpStartFailed, AcpPromptEnded, AcpPromptFailed, AcpSteerAnswered);
