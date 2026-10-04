using System;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// A chunk of a process's output, as raw bytes: output isn't guaranteed to be text. Standard output
/// and standard error share one sequence of offsets, so chunks interleave in the order they were written.
/// </summary>
/// <param name="ProcessId">The process.</param>
/// <param name="Offset">The position of the chunk's first byte in the process's output.</param>
/// <param name="Channel">Which output it was written to.</param>
/// <param name="Data">The bytes, at most 64 KiB per chunk.</param>
public sealed record ProcessOutput(ProcessId ProcessId, long Offset, OutputChannel Channel, ReadOnlyMemory<byte> Data);
