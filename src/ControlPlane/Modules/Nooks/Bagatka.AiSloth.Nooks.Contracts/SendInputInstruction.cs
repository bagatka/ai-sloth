using System;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Write bytes to a running process's standard input.
/// </summary>
/// <param name="ProcessId">The process.</param>
/// <param name="Data">The bytes, at most 64 KiB.</param>
public sealed record SendInputInstruction(ProcessId ProcessId, ReadOnlyMemory<byte> Data);
