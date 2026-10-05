using System;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Input to <see cref="INooksApi.WakeAsync"/>.
/// </summary>
/// <param name="NookId">The nook.</param>
/// <param name="KeepAwakeFor">
/// How long it stays awake from now at least, up to an hour, such as while an agent works and renews
/// it; <see langword="null"/> for the nook's sleep period, as after any use.
/// </param>
public sealed record WakeNook(NookId NookId, TimeSpan? KeepAwakeFor);
