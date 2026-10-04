using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagatka.Harnesses;

/// <summary>
/// A program that runs a coding agent and speaks the Agent Client Protocol on its standard input and
/// output: how to start it, and which credentials it takes and how.
/// </summary>
/// <param name="Id">A stable identifier, such as <c>claude-code</c>.</param>
/// <param name="Name">Its name for people.</param>
/// <param name="Command">The program, found on the PATH.</param>
/// <param name="Arguments">Its arguments.</param>
/// <param name="Credentials">The credentials it takes, and how.</param>
/// <param name="Environment">Variables it always gets.</param>
public sealed record HarnessProfile(
    string Id,
    string Name,
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<CredentialUse> Credentials,
    IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>Whether the harness takes this kind of credential.</summary>
    public bool Accepts(CredentialKind kind)
    {
        return Credentials.Any(use => use.Kind == kind);
    }

    /// <summary>Whether this kind of credential stays behind a model gateway instead of reaching the harness.</summary>
    public bool UsesGateway(CredentialKind kind)
    {
        return Use(kind).GatewayVariable is not null;
    }

    /// <summary>
    /// The environment to start the harness with. <paramref name="value"/> is the secret, or the
    /// gateway's token when <see cref="UsesGateway"/>; then <paramref name="gateway"/> is required.
    /// </summary>
    public IReadOnlyDictionary<string, string> EnvironmentFor(CredentialKind kind, string value, Uri? gateway)
    {
        CredentialUse use = Use(kind);
        Dictionary<string, string> environment = new Dictionary<string, string>(Environment, StringComparer.Ordinal) { [use.Variable] = value };
        if (use.GatewayVariable is string gatewayVariable)
        {
            ArgumentNullException.ThrowIfNull(gateway);
            environment[gatewayVariable] = gateway.AbsoluteUri.TrimEnd('/');
        }

        return environment;
    }

    private CredentialUse Use(CredentialKind kind)
    {
        CredentialUse? use = Credentials.SingleOrDefault(candidate => candidate.Kind == kind);
        if (use is null)
        {
            throw new ArgumentException(Name + " doesn't take " + kind + ".", nameof(kind));
        }

        return use;
    }
}
