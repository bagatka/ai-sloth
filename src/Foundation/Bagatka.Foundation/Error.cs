using System;
using System.Collections.Generic;

namespace Bagatka.Foundation;

/// <summary>
/// An expected failure: rejected input, a denied call, missing data, or a conflict. Bugs and
/// infrastructure failures are exceptions, never errors. Clients branch on and localize by
/// <see cref="Code"/>, never by <see cref="Message"/>.
/// </summary>
public sealed class Error
{
    private Error(ErrorKind kind, string code, string message, IReadOnlyList<FieldError> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Kind = kind;
        Code = code;
        Message = message;
        Fields = fields;
    }

    /// <summary>The caller is not authenticated.</summary>
    public static Error Unauthorized { get; } =
        new Error(ErrorKind.Unauthorized, "unauthorized", "The caller is not authenticated.", []);

    /// <summary>The actor may not perform the operation.</summary>
    public static Error Forbidden { get; } =
        new Error(ErrorKind.Forbidden, "forbidden", "The actor may not perform this operation.", []);

    /// <summary>The category, which edges map to their transport.</summary>
    public ErrorKind Kind { get; }

    /// <summary>A stable identifier such as <c>users.not_found</c>.</summary>
    public string Code { get; }

    /// <summary>A developer-facing English explanation.</summary>
    public string Message { get; }

    /// <summary>The rejected fields. Empty unless <see cref="Kind"/> is <see cref="ErrorKind.Validation"/>.</summary>
    public IReadOnlyList<FieldError> Fields { get; }

    /// <summary>Rejects one input field.</summary>
    /// <param name="field">The camelCase name of the input field.</param>
    /// <param name="message">Why the value was rejected.</param>
    public static Error Validation(string field, string message)
    {
        return new Error(
            ErrorKind.Validation,
            "validation",
            "One or more inputs are invalid.",
            [new FieldError(field, message)]);
    }

    /// <summary>The target does not exist, or the actor may not learn that it exists.</summary>
    public static Error NotFound(string code, string message)
    {
        return new Error(ErrorKind.NotFound, code, message, []);
    }

    /// <summary>The actor may not perform the operation, for a reason worth telling them.</summary>
    public static Error NotAllowed(string code, string message)
    {
        return new Error(ErrorKind.Forbidden, code, message, []);
    }

    /// <summary>The operation conflicts with the current state.</summary>
    public static Error Conflict(string code, string message)
    {
        return new Error(ErrorKind.Conflict, code, message, []);
    }
}
