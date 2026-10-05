namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// What every agent of a person's chats in a workspace is told to follow, besides each repository's
/// own instructions: the workspace's first, then the person's own. Empty for none.
/// </summary>
/// <param name="Workspace">The workspace's, for everyone's chats there.</param>
/// <param name="Personal">The person's own, for the chats they start.</param>
public sealed record Instructions(string Workspace, string Personal);
