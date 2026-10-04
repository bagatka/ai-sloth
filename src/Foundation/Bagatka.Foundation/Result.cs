using System;
using System.Diagnostics.CodeAnalysis;

namespace Bagatka.Foundation;

/// <summary>
/// The outcome of an operation that returns nothing when it succeeds: <see cref="Success"/> or an
/// expected <see cref="Error"/>. Create it explicitly with <c>new Result(new Success())</c> or
/// <c>new Result(error)</c>.
/// </summary>
public union Result(Success, Error)
{
    /// <summary>
    /// Returns <see langword="true"/> and the error when this result failed.
    /// </summary>
    /// <exception cref="InvalidOperationException">The result is <c>default</c>, which is a bug.</exception>
    public bool IsError([NotNullWhen(true)] out Error? error)
    {
        switch (Value)
        {
            case Success:
                error = null;
                return false;
            case Error failed:
                error = failed;
                return true;
            default:
                throw new InvalidOperationException("Result is default; create it with new Result(...).");
        }
    }
}
