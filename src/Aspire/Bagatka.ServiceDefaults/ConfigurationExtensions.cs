using System;
using Microsoft.Extensions.Configuration;

namespace Bagatka.ServiceDefaults;

/// <summary>
/// How hosts turn configuration into an owner's settings record (PATTERNS.md, entry 20).
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Binds a section to a settings record through its constructor. Startup fails, naming the
    /// section, when the section is missing, a value is missing or invalid, or a key is unknown, which
    /// catches typos.
    /// </summary>
    public static T GetRequired<T>(this IConfiguration configuration, string section)
        where T : class
    {
        IConfigurationSection found = configuration.GetSection(section);
        if (!found.Exists())
        {
            throw new InvalidOperationException("Configuration section '" + section + "' is missing.");
        }

        try
        {
            return found.Get<T>(binder => binder.ErrorOnUnknownConfiguration = true)
                ?? throw new InvalidOperationException("Configuration section '" + section + "' is empty.");
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            throw new InvalidOperationException("Configuration section '" + section + "' is invalid: " + exception.Message, exception);
        }
    }
}
