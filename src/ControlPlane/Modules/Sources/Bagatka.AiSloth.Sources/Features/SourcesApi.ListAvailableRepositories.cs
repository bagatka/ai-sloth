using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.Foundation;
using Bagatka.Sdk.GitHub;

namespace Bagatka.AiSloth.Sources;

internal sealed partial class SourcesApi
{
    public async Task<Result<AvailableRepositories>> ListAvailableRepositoriesAsync(Actor actor, CancellationToken ct)
    {
        Result<SourcesGitHubApp> app = App();
        if (app.Failed)
        {
            return new Result<AvailableRepositories>(app.Error);
        }

        Result<Connected> connected = await ConnectedAsync(actor, ct);
        if (connected.Failed)
        {
            return new Result<AvailableRepositories>(connected.Error);
        }

        List<GitHubRepository> repositories = [];
        try
        {
            IReadOnlyList<GitHubInstallation> installations = await github.ListUserInstallationsAsync(connected.Output.Token, ct);
            foreach (GitHubInstallation installation in installations)
            {
                IReadOnlyList<GitHubRepository> installed = await github.ListInstallationRepositoriesAsync(connected.Output.Token, installation.Id, ct);
                repositories.AddRange(installed);
            }
        }
        catch (HttpRequestException exception) when (Revoked(exception))
        {
            return new Result<AvailableRepositories>(SourcesErrors.GitHubNotConnected);
        }

        List<AvailableRepository> available = [.. repositories
            .Select(repository => new AvailableRepository(repository.FullName, repository.Private))
            .DistinctBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)];
        return new Result<AvailableRepositories>(new AvailableRepositories(available, github.InstallationUrl(app.Output.Slug)));
    }
}
