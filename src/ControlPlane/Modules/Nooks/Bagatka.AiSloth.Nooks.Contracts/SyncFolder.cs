namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Input to <see cref="INooksApi.SyncFolderAsync"/>.
/// </summary>
/// <param name="NookId">The nook.</param>
/// <param name="Path">The absolute path of the folder in the nook, such as <c>/root/.claude/memory</c>.</param>
/// <param name="Folder">
/// The name of the folder AiSloth keeps, chosen by the caller: at most 200 lowercase letters, digits,
/// <c>-</c>, <c>.</c>, and <c>/</c>, such as <c>harness-state/&lt;workspace&gt;/&lt;person&gt;/claude-code</c>.
/// </param>
public sealed record SyncFolder(NookId NookId, string Path, string Folder);
