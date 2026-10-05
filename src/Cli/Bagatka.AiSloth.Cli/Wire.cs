using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Bagatka.AiSloth.Cli;

// The shapes of a host's public HTTP API as sloth sends and reads them, with only the fields it uses.
internal static class Wire
{
    internal sealed record HostDiscovery(string Name, int ApiVersion, SignInMethods SignIn);

    internal sealed record SignInMethods(string? Provider, bool InviteSignUp);

    internal sealed record CodeSignIn(string Code, string Device, string? Name);

    internal sealed record TokenExchange(string Code, string CodeVerifier, Uri RedirectUri);

    internal sealed record SignedIn(string Token, Guid Session, Person User, Guid? Workspace);

    internal sealed record Person(Guid Id, string Name);

    internal sealed record LinkCode(string Code, DateTimeOffset ExpiresAt);

    internal sealed record Workspace(Guid Id, string Name, string Access);

    internal sealed record WorkspacePage(IReadOnlyList<Workspace> Items, string? NextCursor);

    internal sealed record CreateInvite(string Access);

    internal sealed record Invite(string Code, DateTimeOffset ExpiresAt);

    internal sealed record AcceptInvite(string Code);

    internal sealed record Resource(string Kind, Guid Id);

    internal sealed record AccountKind(string Kind, bool AddedBySignIn, bool PersonalOnly, bool Allowed);

    internal sealed record Account(Guid Id, string Kind, string Name, Guid? WorkspaceId, Uri? Endpoint, bool NeedsSignIn);

    internal sealed record AddAccount(string Kind, string Name, string Secret, Uri? Endpoint);

    internal sealed record StartAccountSignIn(string Kind, string Name, Uri Callback);

    internal sealed record AccountSignInStarted(Guid Id, Uri Url, DateTimeOffset ExpiresAt);

    internal sealed record CompleteAccountSignIn(Uri ReturnedTo);

    internal sealed record Harness(string Id, string Name, IReadOnlyList<string> Accepts);

    internal sealed record Provider(string Id, string Name, bool Available);

    internal sealed record Secret(string Name, DateTimeOffset SetAt);

    internal sealed record SetSecret(string Value);

    internal sealed record Chat(Guid Id, Guid NookId, Guid StartedBy, DateTimeOffset StartedAt, bool Working, string Harness, Guid Account);

    internal sealed record Nook(Guid Id, IReadOnlyList<NookSource> Sources);

    internal sealed record NookSource(string Name, string? Branch, string? Commit);

    internal sealed record NookRepository(Guid Repository, string? Branch);

    internal sealed record GitHubConnectionStarted(Guid Id, string UserCode, Uri VerificationUri, DateTimeOffset ExpiresAt, TimeSpan Interval);

    internal sealed record GitHubAccount(string Login);

    internal sealed record GitHubConnectionProgress(GitHubAccount? Account, TimeSpan RetryAfter);

    internal sealed record AvailableRepository(string FullName, bool Private);

    internal sealed record AvailableRepositories(IReadOnlyList<AvailableRepository> Repositories, Uri InstallUrl);

    internal sealed record Repository(Guid Id, string Name, string FullName, string DefaultBranch, Uri Url);

    internal sealed record AddRepository(string FullName);

    internal sealed record GitIdentity(string Name, string Email);

    internal sealed record CommitIdentity(GitIdentity Author, GitIdentity Committer, string? CoAuthor);

    internal sealed record GitSettings(GitIdentity? Author, GitIdentity? Committer, bool AiSlothCoAuthor, string BranchPrefix, CommitIdentity? Effective);

    internal sealed record SetGitSettings(GitIdentity? Author, GitIdentity? Committer, bool AiSlothCoAuthor, string BranchPrefix);

    internal sealed record Push(IReadOnlyList<string>? Sources, string? Branch, bool PullRequest, string? Message);

    internal sealed record PushedSource(string Source, string? Branch, int Commits, Uri? BranchUrl, Uri? PullRequestUrl, string? Problem);


    internal sealed record ChatPage(IReadOnlyList<Chat> Items, string? NextCursor);

    internal sealed record Checkpoint(int Number, DateTimeOffset CreatedAt, string Note);

    internal sealed record CheckpointPage(IReadOnlyList<Checkpoint> Items, string? NextCursor);

    internal sealed record StartChat(string Provider, string Harness, Guid Account, IReadOnlyList<NookRepository> Repositories, Guid? CopyOf, int? Checkpoint);

    internal sealed record SendMessage(string Text, bool ConfirmNearlyFullDisk);

    internal sealed record HarnessState(string Harness, DateTimeOffset SavedAt, long Bytes, Guid SavedFrom);

    internal sealed record Instructions(string Workspace, string Personal);

    internal sealed record SetInstructions(string Text);

    internal sealed record SentMessage(Guid Id, bool IsProposal);

    internal sealed record ChatEvent(long Sequence, DateTimeOffset At, JsonElement Event);

    internal sealed record Problem(string? Title, string? Code, Dictionary<string, string[]>? Errors);
}
