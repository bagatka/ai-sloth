using Bagatka.AiSloth.Sources.Contracts;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>Input to <see cref="INooksApi.ExportChangesAsync"/>.</summary>
/// <param name="NookId">The nook.</param>
/// <param name="Source">The source, by name.</param>
/// <param name="Identity">Who the commit of changes not yet committed names, and the co-author line it gets.</param>
/// <param name="Message">That commit's message.</param>
public sealed record ExportChanges(NookId NookId, string Source, CommitIdentity Identity, string Message);
