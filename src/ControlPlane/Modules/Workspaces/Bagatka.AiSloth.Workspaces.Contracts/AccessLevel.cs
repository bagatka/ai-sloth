namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>
/// How much a person may do with a resource. Each level includes the ones below it, so compare
/// levels: <c>access &gt;= AccessLevel.Write</c>. What a level allows for its own data, each module
/// decides.
/// </summary>
public enum AccessLevel
{
    /// <summary>Sees the resource and everything in it.</summary>
    Read = 1,

    /// <summary>Works in it: creates, changes, and runs things.</summary>
    Write = 2,

    /// <summary>Decides who else has access, and manages the resource itself.</summary>
    Manage = 3,
}
