using System.Threading;
using System.Threading.Tasks;
using Bagatka.Sdk.GitHub;

namespace Bagatka.AiSloth.Sources;

// The co-author line that credits AiSloth: the host's GitHub App's bot, by the no-reply address GitHub
// links to the app. Looked up once per process, since it never changes; a host without an app, or an
// app GitHub doesn't find, gets none.
internal sealed class CoAuthorLine(GitHubClient github, SourcesSettings settings)
{
    private string? _line;

    public async Task<string?> GetAsync(string token, CancellationToken ct)
    {
        if (_line is not null || settings.GitHubApp is null)
        {
            return _line;
        }

        GitHubUser? bot = await github.GetUserAsync(token, settings.GitHubApp.Slug + "[bot]", ct);
        _line = bot is null ? null : "Co-authored-by: AiSloth <" + bot.NoReplyEmail + ">";
        return _line;
    }
}
