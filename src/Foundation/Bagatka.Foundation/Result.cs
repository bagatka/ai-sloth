using System.Diagnostics.CodeAnalysis;

namespace Bagatka.Foundation;

/// <summary>
/// The outcome of an operation that returns nothing when it succeeds: <see cref="Success"/> or an
/// expected <see cref="Error"/>. Create it explicitly with <c>new Result(new Success())</c> or
/// <c>new Result(error)</c>.
/// </summary>
/// <remarks>Check <see cref="Failed"/> before reading <see cref="Error"/>; the compiler refuses it until then.</remarks>
public union Result(Success, Error)
{
    /// <summary>Whether the operation failed: then <see cref="Error"/> is set.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool Failed => Value is Error;

    /// <summary>The error, when the operation failed; otherwise <see langword="null"/>.</summary>
    public Error? Error => Value as Error;
}
