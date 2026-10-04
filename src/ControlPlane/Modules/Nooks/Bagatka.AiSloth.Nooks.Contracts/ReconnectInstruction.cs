namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// This control-plane instance is shutting down: reconnect now, and the next connection reaches
/// another instance. Running processes are unaffected.
/// </summary>
public sealed record ReconnectInstruction;
