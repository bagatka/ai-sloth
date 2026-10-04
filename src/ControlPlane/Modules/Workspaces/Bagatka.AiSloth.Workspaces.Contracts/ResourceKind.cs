namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>The kinds of <see cref="Resource"/> people can be given access to.</summary>
public enum ResourceKind
{
    /// <summary>A workspace; access to it reaches everything in it.</summary>
    Workspace = 1,

    /// <summary>A nook, with its processes and chats.</summary>
    Nook = 2,
}
