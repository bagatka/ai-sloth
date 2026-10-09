using System;
using System.Collections.Generic;

namespace Bagatka.Foundation;

/// <summary>
/// Something a person did, or what it led to, such as a chat started or a turn ended: named in the
/// past tense and snake_case, with plain facts about it, such as kinds, counts, durations, and
/// outcomes. Never what people wrote, names, repositories, or secrets; the person and the workspace
/// appear only as their IDs.
/// </summary>
/// <param name="Name">The event's name, such as <c>chat_started</c>.</param>
/// <param name="Person">Who did it, or whom it happened to.</param>
/// <param name="Workspace">The workspace it happened in, when there is one.</param>
/// <param name="Facts">Its facts, by snake_case names, such as <c>harness</c>.</param>
public sealed record ProductEvent(string Name, UserId Person, Guid? Workspace, IReadOnlyDictionary<string, ProductFact> Facts);
