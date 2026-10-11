using System.Collections.Generic;

namespace Bagatka.Sdk.GitHub;

// The shapes GitHub sends and takes, with only the fields this client reads; GitHubJsonContext maps
// their names to snake_case.
internal static class GitHubWire
{
    internal sealed record DeviceCodeResponse(string? DeviceCode, string? UserCode, string? VerificationUri, int? ExpiresIn, int? Interval, string? Error);

    internal sealed record TokenResponse(
        string? AccessToken,
        int? ExpiresIn,
        string? RefreshToken,
        int? RefreshTokenExpiresIn,
        string? Error,
        string? ErrorDescription,
        int? Interval);

    internal sealed record User(long Id, string Login, string? Name);

    internal sealed record Account(string Login);

    internal sealed record Installation(long Id, Account Account);

    internal sealed record InstallationPage(IReadOnlyList<Installation> Installations);

    internal sealed record Repository(string Name, string FullName, Account Owner, bool Private, string DefaultBranch, string CloneUrl, string HtmlUrl);

    internal sealed record RepositoryPage(IReadOnlyList<Repository> Repositories);

    internal sealed record PullRequest(int Number, string HtmlUrl);

    internal sealed record NewPullRequest(string Title, string Head, string Base, string Body);

    internal sealed record Problem(string? Message, IReadOnlyList<ProblemDetail>? Errors);

    internal sealed record ProblemDetail(string? Message);

    // Copilot's API, which doesn't document this answer: what a client reads of it is all optional.
    internal sealed record CopilotModelList(IReadOnlyList<CopilotModel> Data);

    internal sealed record CopilotModel(string Id, string? Name, CopilotCapabilities? Capabilities, CopilotPolicy? Policy, IReadOnlyList<string>? SupportedEndpoints);

    internal sealed record CopilotCapabilities(string? Type, CopilotSupports? Supports, CopilotLimits? Limits);

    internal sealed record CopilotLimits(long? MaxContextWindowTokens, long? MaxOutputTokens);

    internal sealed record CopilotSupports(bool? ToolCalls, IReadOnlyList<string>? ReasoningEffort);

    internal sealed record CopilotPolicy(string? State);

    // copilot_internal/user, which Copilot doesn't document either: only the quotas are read.
    internal sealed record CopilotUser(string? QuotaResetDateUtc, IReadOnlyDictionary<string, CopilotQuotaSnapshot>? QuotaSnapshots);

    internal sealed record CopilotQuotaSnapshot(decimal? Entitlement, decimal? Remaining, bool? Unlimited, decimal? CreditsUsed);
}
