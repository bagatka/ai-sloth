using System;
using System.Diagnostics.CodeAnalysis;

namespace Bagatka.Foundation;

/// <summary>
/// The outcome of an operation that produces a <typeparamref name="T"/> when it succeeds, or an
/// expected <see cref="Error"/>. Create it explicitly with <c>new Result&lt;T&gt;(value)</c> or
/// <c>new Result&lt;T&gt;(error)</c>.
/// </summary>
/// <typeparam name="T">The value produced on success.</typeparam>
public union Result<T>(T, Error)
    where T : notnull
{
    /// <summary>
    /// Returns <see langword="true"/> and the value when this result succeeded; otherwise
    /// <see langword="false"/> and the error. The compiler tracks which one is set.
    /// </summary>
    /// <exception cref="InvalidOperationException">The result is <c>default</c>, which is a bug.</exception>
    public bool TryGetValue([MaybeNullWhen(false)] out T value, [NotNullWhen(false)] out Error? error)
    {
        switch (Value)
        {
            case T found:
                value = found;
                error = null;
                return true;
            case Error failed:
                value = default;
                error = failed;
                return false;
            default:
                throw new InvalidOperationException("Result is default; create it with new Result<T>(...).");
        }
    }
}
