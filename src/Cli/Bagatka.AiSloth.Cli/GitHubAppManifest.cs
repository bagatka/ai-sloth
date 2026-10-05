using System;
using System.Collections.Generic;

namespace Bagatka.AiSloth.Cli;

// GitHub's app manifest flow, in GitHub's snake_case: what a new GitHub App is, and what GitHub
// answers once it made one (https://docs.github.com/apps/sharing-github-apps/registering-a-github-app-from-a-manifest).
internal static class GitHubAppManifest
{
    internal sealed record Manifest(
        string Name,
        Uri Url,
        Hook HookAttributes,
        Uri RedirectUrl,
        bool Public,
        IReadOnlyDictionary<string, string> DefaultPermissions,
        bool RequestOauthOnInstall);

    internal sealed record Hook(Uri Url, bool Active);

    // The private key and webhook secret GitHub also sends aren't kept: AiSloth acts only as people.
    internal sealed record Conversion(string Slug, string ClientId, string ClientSecret, Uri HtmlUrl);
}
