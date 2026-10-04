using System;
using System.Diagnostics.CodeAnalysis;

namespace Bagatka.Foundation;

/// <summary>
/// The outcome of an operation that produces a <typeparamref name="T"/> when it succeeds, or an
/// expected <see cref="Error"/>. Create it explicitly with <c>new Result&lt;T&gt;(value)</c> or
/// <c>new Result&lt;T&gt;(error)</c>.
/// </summary>
/// <remarks>
/// Check <see cref="Failed"/> before reading: the compiler refuses <see cref="Output"/> and
/// <see cref="Error"/> until it knows which one is set.
/// </remarks>
/// <typeparam name="T">The value produced on success.</typeparam>
public union Result<T>(T, Error)
    where T : notnull
{
    /// <summary>Whether the operation failed: then <see cref="Error"/> is set; otherwise <see cref="Output"/> is.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    [MemberNotNullWhen(false, nameof(Output))]
    public bool Failed => Value is Error;

    /// <summary>The value, when the operation succeeded. Reading it after a failure throws, which is a bug.</summary>
    /// <exception cref="InvalidOperationException">The result failed, or is <c>default</c>.</exception>
    public T? Output => Value switch
    {
        T output => output,
        null => throw new InvalidOperationException("Result is default; create it with new Result<T>(...)."),
        _ => throw new InvalidOperationException("The result failed; check Failed before reading Output."),
    };

    /// <summary>The error, when the operation failed; otherwise <see langword="null"/>.</summary>
    public Error? Error => Value as Error;
}
