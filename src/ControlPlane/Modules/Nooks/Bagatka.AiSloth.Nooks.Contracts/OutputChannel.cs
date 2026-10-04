namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Which output a process wrote to.
/// </summary>
public enum OutputChannel
{
    /// <summary>Standard output.</summary>
    StandardOutput = 1,

    /// <summary>Standard error.</summary>
    StandardError = 2,
}
