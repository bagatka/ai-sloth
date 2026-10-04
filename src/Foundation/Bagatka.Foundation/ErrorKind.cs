namespace Bagatka.Foundation;

/// <summary>
/// The category of an <see cref="Error"/>. Edges map each kind to their transport in one place,
/// for example HTTP status codes in the WebApi.
/// </summary>
public enum ErrorKind
{
    /// <summary>Input was rejected; <see cref="Error.Fields"/> says which fields and why.</summary>
    Validation = 1,

    /// <summary>The caller is not authenticated.</summary>
    Unauthorized = 2,

    /// <summary>The actor may not perform the operation.</summary>
    Forbidden = 3,

    /// <summary>The target does not exist, or the actor may not learn that it exists.</summary>
    NotFound = 4,

    /// <summary>The operation conflicts with the current state.</summary>
    Conflict = 5,
}
