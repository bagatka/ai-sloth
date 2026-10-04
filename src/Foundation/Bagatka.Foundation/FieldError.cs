namespace Bagatka.Foundation;

/// <summary>
/// Why one input field was rejected.
/// </summary>
/// <param name="Field">The camelCase name of the input field, for example <c>displayName</c>.</param>
/// <param name="Message">A developer-facing English explanation.</param>
public sealed record FieldError(string Field, string Message);
