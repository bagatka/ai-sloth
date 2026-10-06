using System;
using System.Collections.Generic;

namespace Bagatka.AiSloth.Cli;

// A release as GitHub's REST API describes it: its tag, such as sloth-v0.2.0, and its files, each with
// its SHA-256 as GitHub computed it ("sha256:<hex>").
internal sealed record GitHubRelease(string TagName, IReadOnlyList<GitHubRelease.Asset> Assets)
{
    internal sealed record Asset(string Name, Uri BrowserDownloadUrl, string? Digest);
}
