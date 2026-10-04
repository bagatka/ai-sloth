using System;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Input to <see cref="INooksApi.SendInputAsync"/>.
/// </summary>
/// <param name="NookId">The nook.</param>
/// <param name="ProcessId">The process.</param>
/// <param name="Data">The bytes to write to its standard input, at most 64 KiB.</param>
public sealed record SendInput(NookId NookId, ProcessId ProcessId, ReadOnlyMemory<byte> Data);
