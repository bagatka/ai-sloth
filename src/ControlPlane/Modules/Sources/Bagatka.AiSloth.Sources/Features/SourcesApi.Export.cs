using System;
using System.IO;
using System.Net.Http;
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
    public async Task<Result<ExportedRepository>> ExportAsync(Actor actor, ExportRepository command, Stream destination, CancellationToken ct)
    {
        Result<Repository> repository = await FindRepositoryAsync(actor, command.Repository, AccessLevel.Read, ct);
        if (repository.Failed)
        {
            return new Result<ExportedRepository>(repository.Error);
        }

        Result<Reached> reached = await ReachAsync(actor, repository.Output, ct);
        if (reached.Failed)
        {
            return new Result<ExportedRepository>(reached.Error);
        }

        string branch = command.Branch ?? reached.Output.Repository.DefaultBranch;
        if (!GitNames.IsBranch(branch))
        {
            return new Result<ExportedRepository>(Error.Validation("branch", "Must be a branch name: letters, digits, /, -, _, and ."));
        }

        // A bare copy of the one branch, with its history, sent on as a bundle the nook clones.
        DirectoryInfo scratch = Directory.CreateTempSubdirectory("aisloth-export-");
        try
        {
            string[] clone = ["clone", "--quiet", "--bare", "--single-branch", "--no-tags", "--branch", branch, "--", reached.Output.Repository.CloneUrl.AbsoluteUri, "repository.git"];
            GitRun cloned = await GitCommand.RunAsync(scratch.FullName, clone, reached.Output.Token, input: null, output: null, ct);
            if (!cloned.Succeeded)
            {
                bool noBranch = cloned.Errors.Contains("not found in upstream", StringComparison.Ordinal);
                return new Result<ExportedRepository>(noBranch
                    ? Error.Validation("branch", repository.Output.Name + " has no branch " + branch + ".")
                    : Error.Conflict("sources.git_failed", "Copying " + reached.Output.Repository.FullName + " failed: " + cloned.Errors));
            }

            string copy = Path.Combine(scratch.FullName, "repository.git");
            GitRun head = await GitCommand.RunAsync(copy, ["rev-parse", "refs/heads/" + branch], token: null, input: null, output: null, ct);
            GitRun bundled = await GitCommand.RunAsync(copy, ["bundle", "create", "-", "refs/heads/" + branch], token: null, input: null, destination, ct);
            if (!head.Succeeded || !bundled.Succeeded)
            {
                throw new InvalidOperationException("Bundling a fresh copy of " + reached.Output.Repository.FullName + " failed: " + head.Errors + bundled.Errors);
            }

            return new Result<ExportedRepository>(new ExportedRepository(repository.Output.Name, branch, head.Output, reached.Output.Repository.CloneUrl));
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    // The repository at GitHub, as the actor's connection sees it now.
    private async Task<Result<Reached>> ReachAsync(Actor actor, Repository repository, CancellationToken ct)
    {
        Result<Connected> connected = await ConnectedAsync(actor, ct);
        if (connected.Failed)
        {
            return new Result<Reached>(connected.Error);
        }

        GitHubRepository? found;
        try
        {
            found = await github.GetRepositoryAsync(connected.Output.Token, repository.Owner, repository.Name, ct);
        }
        catch (HttpRequestException exception) when (Revoked(exception))
        {
            return new Result<Reached>(SourcesErrors.GitHubNotConnected);
        }

        return found is null ? new Result<Reached>(SourcesErrors.RepositoryUnreachable) : new Result<Reached>(new Reached(found, connected.Output.Token));
    }

    // A repository at GitHub and the token that reached it. Never log it.
    private sealed record Reached(GitHubRepository Repository, string Token)
    {
        public override string ToString()
        {
            return "Reached { Repository = " + Repository.FullName + ", Token = *** }";
        }
    }
}
