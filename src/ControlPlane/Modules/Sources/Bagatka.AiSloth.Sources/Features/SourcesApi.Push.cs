using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Sources.Git;
using Bagatka.AiSloth.Sources.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Sdk.GitHub;

namespace Bagatka.AiSloth.Sources;

internal sealed partial class SourcesApi
{
    public async Task<Result<PushedChanges>> PushAsync(Actor actor, PushChanges command, Stream bundle, CancellationToken ct)
    {
        Result<Repository> repository = await FindRepositoryAsync(actor, command.Repository, AccessLevel.Write, ct);
        if (repository.Failed)
        {
            return new Result<PushedChanges>(repository.Error);
        }

        if (!GitNames.IsBranch(command.Branch) || !GitNames.IsBranch(command.BaseBranch))
        {
            return new Result<PushedChanges>(Error.Validation("branch", "Must be a branch name: letters, digits, /, -, _, and ."));
        }

        Result<Reached> reached = await ReachAsync(actor, repository.Output, ct);
        if (reached.Failed)
        {
            return new Result<PushedChanges>(reached.Error);
        }

        GitHubRepository remote = reached.Output.Repository;
        if (string.Equals(command.Branch, remote.DefaultBranch, StringComparison.Ordinal))
        {
            return new Result<PushedChanges>(Error.Validation("branch", "AiSloth never pushes to " + remote.FullName + "'s default branch, " + remote.DefaultBranch + "; name another."));
        }

        DirectoryInfo scratch = Directory.CreateTempSubdirectory("aisloth-push-");
        try
        {
            Result<Delivered> delivered = await DeliverAsync(scratch.FullName, remote, command, reached.Output.Token, bundle, ct);
            if (delivered.Failed)
            {
                return new Result<PushedChanges>(delivered.Error);
            }

            Uri? pullRequest = null;
            if (command.PullRequest)
            {
                Result<GitHubPullRequest> opened = await OpenPullRequestAsync(remote, command, delivered.Output.Title, reached.Output.Token, ct);
                if (opened.Failed)
                {
                    return new Result<PushedChanges>(opened.Error);
                }

                pullRequest = opened.Output.HtmlUrl;
            }

            Uri branchUrl = new Uri(remote.HtmlUrl.AbsoluteUri.TrimEnd('/') + "/tree/" + command.Branch);
            return new Result<PushedChanges>(new PushedChanges(command.Branch, delivered.Output.Commits, branchUrl, pullRequest));
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    // Puts the bundle's commits on top of the base fetched from GitHub, and pushes them to the branch:
    // creating it, or moving it forward when the changes include everything on it. Never forced.
    private static async Task<Result<Delivered>> DeliverAsync(string scratch, GitHubRepository remote, PushChanges command, string token, Stream bundle, CancellationToken ct)
    {
        string changes = Path.Combine(scratch, "changes.bundle");
        await using (FileStream file = File.Create(changes))
        {
            await bundle.CopyToAsync(file, ct);
        }

        string url = remote.CloneUrl.AbsoluteUri;
        string copy = Path.Combine(scratch, "repository.git");
        _ = await GitCommand.RunAsync(scratch, ["init", "--quiet", "--bare", copy], token: null, input: null, output: null, ct);
        GitRun based = await GitCommand.RunAsync(copy, ["fetch", "--quiet", "--no-tags", "--", url, "+refs/heads/" + command.BaseBranch + ":refs/remotes/origin/base"], token, input: null, output: null, ct);
        GitRun hasBase = await GitCommand.RunAsync(copy, ["cat-file", "-e", command.BaseCommit + "^{commit}"], token: null, input: null, output: null, ct);
        if (!based.Succeeded || !hasBase.Succeeded)
        {
            return new Result<Delivered>(Error.Conflict("sources.base_gone", "The commit the nook started from is no longer on " + remote.FullName + "'s " + command.BaseBranch + ". " + based.Errors));
        }

        GitRun fetched = await GitCommand.RunAsync(copy, ["fetch", "--quiet", "--no-tags", changes, "+HEAD:refs/heads/changes"], token: null, input: null, output: null, ct);
        GitRun head = await GitCommand.RunAsync(copy, ["rev-parse", "refs/heads/changes"], token: null, input: null, output: null, ct);
        if (!fetched.Succeeded || !head.Succeeded)
        {
            return new Result<Delivered>(Error.Validation("changes", "The nook's changes aren't a git bundle on top of " + command.BaseCommit + ". " + fetched.Errors));
        }

        Result moved = await MayMoveAsync(copy, url, command.Branch, head.Output, token, ct);
        if (moved.Failed)
        {
            return new Result<Delivered>(moved.Error);
        }

        GitRun pushed = await GitCommand.RunAsync(copy, ["push", "--quiet", "--", url, "refs/heads/changes:refs/heads/" + command.Branch], token, input: null, output: null, ct);
        if (!pushed.Succeeded)
        {
            bool behind = pushed.Errors.Contains("rejected", StringComparison.Ordinal);
            return new Result<Delivered>(behind ? SourcesErrors.NotFastForward : Error.Conflict("sources.git_failed", "Pushing to " + remote.FullName + " failed: " + pushed.Errors));
        }

        GitRun counted = await GitCommand.RunAsync(copy, ["rev-list", "--count", command.BaseCommit + "..refs/heads/changes"], token: null, input: null, output: null, ct);
        GitRun subject = await GitCommand.RunAsync(copy, ["log", "-1", "--format=%s", "refs/heads/changes"], token: null, input: null, output: null, ct);
        int commits = int.Parse(counted.Output, System.Globalization.CultureInfo.InvariantCulture);
        return new Result<Delivered>(new Delivered(commits, subject.Output));
    }

    // A branch that exists moves only forward: everything on it must be among the changes.
    private static async Task<Result> MayMoveAsync(string copy, string url, string branch, string head, string token, CancellationToken ct)
    {
        GitRun listed = await GitCommand.RunAsync(copy, ["ls-remote", "--heads", "--", url, "refs/heads/" + branch], token, input: null, output: null, ct);
        string[] found = listed.Output.Split('\t');
        if (!listed.Succeeded || found is not [{ Length: > 0 } current, _] || string.Equals(current, head, StringComparison.Ordinal))
        {
            return new Result(new Success());
        }

        GitRun fetched = await GitCommand.RunAsync(copy, ["fetch", "--quiet", "--no-tags", "--", url, "+refs/heads/" + branch + ":refs/remotes/origin/current"], token, input: null, output: null, ct);
        GitRun ancestor = await GitCommand.RunAsync(copy, ["merge-base", "--is-ancestor", current, head], token: null, input: null, output: null, ct);
        return fetched.Succeeded && ancestor.Succeeded ? new Result(new Success()) : new Result(SourcesErrors.NotFastForward);
    }

    // The branch's open pull request, or a new one titled after its newest commit.
    private async Task<Result<GitHubPullRequest>> OpenPullRequestAsync(GitHubRepository remote, PushChanges command, string title, string token, CancellationToken ct)
    {
        GitHubPullRequest? open = await github.FindOpenPullRequestAsync(token, remote.Owner, remote.Name, command.Branch, ct);
        if (open is not null)
        {
            return new Result<GitHubPullRequest>(open);
        }

        GitHubNewPullRequest pullRequest = new GitHubNewPullRequest(title.Length > 0 ? title : command.Branch, command.Branch, command.BaseBranch, command.Description);
        return await github.CreatePullRequestAsync(token, remote.Owner, remote.Name, pullRequest, ct);
    }

    // What a push delivered: how many commits the branch has beyond its base, and the newest's subject.
    private sealed record Delivered(int Commits, string Title);
}
