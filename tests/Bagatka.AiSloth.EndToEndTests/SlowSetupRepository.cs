using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Sources.Contracts;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A person's workspace with a GitHub repository, <c>api</c>, whose setup is slow the first time and
/// fast once its dependencies are installed, as installing them is: what earns a ready copy.
/// </summary>
internal sealed class SlowSetupRepository
{
    // Longer than a setup must take to leave a ready copy.
    public const int SetupSeconds = 16;

    private SlowSetupRepository(TestWorkspace workspace, string owner, RepositoryId repository)
    {
        Workspace = workspace;
        Owner = owner;
        Repository = repository;
    }

    public TestWorkspace Workspace { get; }

    public string Owner { get; }

    public RepositoryId Repository { get; }

    /// <summary>The person connects GitHub, and their new workspace adds the repository.</summary>
    public static async Task<SlowSetupRepository> CreateAsync(ControlPlane app, HttpClient person)
    {
        string suffix = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)[^8..];
        string login = "dev-" + suffix;
        string owner = "acme-" + suffix;
        await app.GitHub.CreateRepositoryAsync(owner, "api", login);
        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [".gitignore"] = "deps/\n",
            [".agents/setup"] = string.Create(CultureInfo.InvariantCulture, $"#!/bin/sh\nset -e\nif [ ! -f deps/installed ]; then sleep {SetupSeconds}; mkdir -p deps; touch deps/installed; fi\n"),
        };
        await app.GitHub.CommitFilesAsync(owner, "api", "main", files, "Add a setup");

        GitHubConnectionStarted started = await Api.ReadAsync<GitHubConnectionStarted>(person.SendPostAsync("/github/connections", new { }), HttpStatusCode.OK);
        app.GitHub.Approve(started.UserCode, login);
        await Api.ReadAsync<GitHubConnectionProgress>(person.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/github/connections/{started.Id.Value}/complete"), new { }), HttpStatusCode.OK);
        TestWorkspace workspace = await TestWorkspace.CreateAsync(app, person);
        RepositorySummary repository = await Api.ReadAsync<RepositorySummary>(person.SendPostAsync(workspace.Path + "/repositories", new { fullName = owner + "/api" }), HttpStatusCode.Created);
        return new SlowSetupRepository(workspace, owner, repository.Id);
    }

    /// <summary>A chat whose nook starts with the repository, on the provider.</summary>
    public async Task<ChatSummary> StartChatAsync(string provider)
    {
        return await Workspace.StartChatAsync(repository: Repository, provider: provider);
    }

    /// <summary>A commit on main after the chats that came before.</summary>
    public async Task MoveOnAsync()
    {
        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal) { ["LATEST.md"] = "latest\n" };
        await Workspace.App.GitHub.CommitFilesAsync(Owner, "api", "main", files, "Move on");
    }
}
