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
/// <param name="SessionPaths">
/// Where in its image it keeps its sessions, as absolute paths: saved with every checkpoint, so a new
/// agent loads the conversation (<see cref="Acp.LoadSession"/>).
/// </param>
/// <param name="InstructionsPath">
/// The file it reads its user's own standing instructions from, in every session, as an absolute
/// path; a host writes instructions it gives every agent there.
/// </param>
/// <param name="StatePaths">
/// Where it keeps what it writes for itself to use in later sessions, such as Claude Code's memory, as
/// absolute paths; empty when it keeps nothing of the kind. Never its sessions, credentials, or what
/// its start script writes.
/// </param>
/// <param name="ModelFamily">
/// What the IDs of the models it's made for hold, such as <c>claude</c>, which lists offer first; null
/// when it's made for none in particular.
/// </param>
/// <param name="ThinkingLevels">
/// How hard it can think, in the words its harness uses, such as <c>high</c>, lightest first, for
/// <c>HARNESS_THINKING</c>.
/// </param>
/// <param name="DefaultThinking">The level, one of <paramref name="ThinkingLevels"/>, an agent thinks at until someone chooses another.</param>
public sealed record HarnessProfile(
    string Id,
    string Name,
    IReadOnlyList<CredentialKind> Credentials,
    IReadOnlyList<string> SessionPaths,
    string InstructionsPath,
    IReadOnlyList<string> StatePaths,
    string? ModelFamily,
    IReadOnlyList<string> ThinkingLevels,
    string DefaultThinking)
{
    /// <summary>The program, on the PATH of the harness's image, that starts it.</summary>
    public const string Command = "harness";

    /// <summary>The longest model ID a harness takes.</summary>
    public const int MaxModelLength = 200;

    /// <summary>Whether the harness takes this kind of credential.</summary>
    public bool Accepts(CredentialKind kind)
    {
        return Credentials.Contains(kind);
    }

    /// <summary>
    /// Whether a model's ID can go to a harness: 1 to <see cref="MaxModelLength"/> letters, digits, and
    /// <c>. _ - : / ~ @ +</c>, as vendors write them, such as <c>openai/gpt-6-luna</c>; nothing a start
    /// script would have to quote.
    /// </summary>
    public static bool IsModelId(string? model)
    {
        return model is { Length: > 0 and <= MaxModelLength }
            && model.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ':' or '/' or '~' or '@' or '+');
    }

    /// <summary>
    /// The variables a harness starts with: <c>HARNESS_CREDENTIAL</c>, the kind (<c>openai</c>,
    /// <c>anthropic</c>, <c>openrouter</c>, <c>github-token</c>, or <c>claude-oauth-token</c>);
    /// <c>HARNESS_TOKEN</c>; for a model API <c>HARNESS_MODEL_URL</c>; <c>HARNESS_MODEL</c>, the model it
    /// runs on; and <c>HARNESS_THINKING</c>, how hard it thinks. A model API
    /// always goes through a model gateway, so <paramref name="token"/> is the gateway's token and
    /// <paramref name="gateway"/> its URL; a token tied to one harness goes to it, with no gateway.
    /// </summary>
    /// <param name="kind">What it pays with.</param>
    /// <param name="token">The gateway's token, or the harness's own.</param>
    /// <param name="gateway">The gateway's URL, for a model API.</param>
    /// <param name="model">The model's ID (<see cref="IsModelId"/>).</param>
    /// <param name="thinking">One of its <see cref="ThinkingLevels"/>.</param>
    public IReadOnlyDictionary<string, string> EnvironmentFor(CredentialKind kind, string token, Uri? gateway, string model, string thinking)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        if (!IsModelId(model))
        {
            throw new ArgumentException("A model's ID takes letters, digits, and . _ - : / ~ @ + only.", nameof(model));
        }

        if (!ThinkingLevels.Contains(thinking, StringComparer.Ordinal))
        {
            throw new ArgumentException(Name + " thinks at " + string.Join(", ", ThinkingLevels) + " only.", nameof(thinking));
        }

        (string name, bool modelApi) = kind switch
        {
            CredentialKind.OpenAIApi => ("openai", true),
            CredentialKind.AnthropicApi => ("anthropic", true),
            CredentialKind.OpenRouterApi => ("openrouter", true),
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
            ["HARNESS_MODEL"] = model,
            ["HARNESS_THINKING"] = thinking,
        };
        if (gateway is not null)
        {
            environment["HARNESS_MODEL_URL"] = gateway.AbsoluteUri.TrimEnd('/');
        }

        return environment;
    }
}
