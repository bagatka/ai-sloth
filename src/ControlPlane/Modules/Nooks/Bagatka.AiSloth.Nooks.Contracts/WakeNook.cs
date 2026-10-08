using System;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Input to <see cref="INooksApi.WakeAsync"/>.
/// </summary>
/// <param name="NookId">The nook.</param>
/// <param name="KeepAwakeFor">
/// How long it stays busy from now at least, up to an hour, such as while an agent works and renews
/// it; <see langword="null"/> for a use now. Either way the nook's sleep period starts after it.
/// </param>
public sealed record WakeNook(NookId NookId, TimeSpan? KeepAwakeFor);
