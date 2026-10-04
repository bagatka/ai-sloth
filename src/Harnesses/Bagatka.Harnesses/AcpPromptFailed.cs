using System;

namespace Bagatka.Harnesses;

/// <summary>The turn a <see cref="Acp.Prompt"/> started ended with an error.</summary>
/// <param name="Prompt">The key the prompt was sent with.</param>
/// <param name="Error">What went wrong, in words for people.</param>
public sealed record AcpPromptFailed(Guid Prompt, string Error);
