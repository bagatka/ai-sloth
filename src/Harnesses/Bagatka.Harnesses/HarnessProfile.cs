using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagatka.Harnesses;

/// <summary>
/// A program that runs a coding agent and speaks the Agent Client Protocol on its standard input and
/// output, and the credentials it takes. Every harness starts the same way: a host's image for it
/// provides <see cref="Command"/>, which reads <see cref="EnvironmentFor"/>'s variables, configures the
/// harness from them, and runs it (<c>src/Harnesses/start/&lt;id&gt;.sh</c>).
/// </summary>
/// <param name="Id">A stable identifier, such as <c>claude-code</c>; its start script is named after it.</param>
/// <param name="Name">Its name for people.</param>
/// <param name="Credentials">The kinds of credential it takes.</param>
public sealed record HarnessProfile(string Id, string Name, IReadOnlyList<CredentialKind> Credentials)
{
    /// <summary>The program, on the PATH of the harness's image, that starts it.</summary>
    public const string Command = "harness";

    /// <summary>Whether the harness takes this kind of credential.</summary>
    public bool Accepts(CredentialKind kind)
    {
        return Credentials.Contains(kind);
    }

    /// <summary>
    /// The variables every harness starts with: <c>HARNESS_CREDENTIAL</c>, the kind
    /// (<c>openai</c>, <c>anthropic</c>, <c>github-token</c>, or <c>claude-oauth-token</c>);
    /// <c>HARNESS_TOKEN</c>; and for a model API <c>HARNESS_MODEL_URL</c>. A model API always goes
    /// through a model gateway, so <paramref name="token"/> is the gateway's token and
    /// <paramref name="gateway"/> its URL; a token tied to one harness goes to it, with no gateway.
    /// </summary>
    public static IReadOnlyDictionary<string, string> EnvironmentFor(CredentialKind kind, string token, Uri? gateway)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        (string name, bool modelApi) = kind switch
        {
            CredentialKind.OpenAIApi => ("openai", true),
            CredentialKind.AnthropicApi => ("anthropic", true),
            CredentialKind.GitHubToken => ("github-token", false),
            CredentialKind.ClaudeOAuthToken => ("claude-oauth-token", false),
        };
        if (modelApi != gateway is not null)
        {
            throw new ArgumentException(modelApi ? "A model API goes through a gateway." : "A harness's own token takes no gateway.", nameof(gateway));
        }

        Dictionary<string, string> environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HARNESS_CREDENTIAL"] = name,
            ["HARNESS_TOKEN"] = token,
        };
        if (gateway is not null)
        {
            environment["HARNESS_MODEL_URL"] = gateway.AbsoluteUri.TrimEnd('/');
        }

        return environment;
    }
}
