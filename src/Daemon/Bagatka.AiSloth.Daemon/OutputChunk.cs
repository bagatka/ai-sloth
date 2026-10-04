using System;
using Bagatka.AiSloth.DaemonProtocol.V1;

namespace Bagatka.AiSloth.Daemon;

/// <summary>
/// Output read from a journal.
/// </summary>
/// <param name="Offset">The position of the first byte in the process's output.</param>
/// <param name="Channel">The channel it was written to.</param>
/// <param name="Data">The bytes.</param>
internal sealed record OutputChunk(long Offset, OutputChannel Channel, ReadOnlyMemory<byte> Data);
