using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A person's workspace with a GitHub repository, <c>api</c>, whose setup is slow the first time and
/// fast once its dependencies are installed, as installing them is: what earns a ready copy.
/// </summary>
internal sealed class SlowSetupRepository
{
    // Longer than a setup must take to leave a ready copy.
    public const int SetupSeconds = 16;

    private readonly ControlPlane _controlPlane;
    private readonly HttpClient _person;

    private SlowSetupRepository(ControlPlane controlPlane, HttpClient person, string owner, WorkspaceId workspace, RepositoryId repository, AgentAccountId account)
    {
        _controlPlane = controlPlane;
        _person = person;
        Owner = owner;
        Workspace = workspace;
        Repository = repository;
        Account = account;
    }

    public string Owner { get; }

    public WorkspaceId Workspace { get; }

    public RepositoryId Repository { get; }

    public AgentAccountId Account { get; }

    /// <summary>The person connects GitHub, and their new workspace adds the repository and an account with the fake model's key.</summary>
    public static async Task<SlowSetupRepository> CreateAsync(ControlPlane controlPlane, HttpClient person)
    {
        string suffix = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)[^8..];
        string login = "dev-" + suffix;
        string owner = "acme-" + suffix;
        await controlPlane.GitHub.CreateRepositoryAsync(owner, "api", login);
        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [".gitignore"] = "deps/\n",
            [".agents/setup"] = string.Create(CultureInfo.InvariantCulture, $"#!/bin/sh\nset -e\nif [ ! -f deps/installed ]; then sleep {SetupSeconds}; mkdir -p deps; touch deps/installed; fi\n"),
        };
        await controlPlane.GitHub.CommitFilesAsync(owner, "api", "main", files, "Add a setup");

        GitHubConnectionStarted started = await Api.ReadAsync<GitHubConnectionStarted>(person.SendPostAsync("/github/connections", new { }), HttpStatusCode.OK);
        controlPlane.GitHub.Approve(started.UserCode, login);
        await Api.ReadAsync<GitHubConnectionProgress>(person.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/github/connections/{started.Id.Value}/complete"), new { }), HttpStatusCode.OK);
        WorkspaceSummary workspace = await Api.ReadAsync<WorkspaceSummary>(person.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        string path = string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}");
        RepositorySummary repository = await Api.ReadAsync<RepositorySummary>(person.SendPostAsync(path + "/repositories", new { fullName = owner + "/api" }), HttpStatusCode.Created);
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(
            person.SendPostAsync(path + "/agent-accounts", new { kind = "AnthropicApiKey", name = "Fake", secret = FakeModel.ApiKey, endpoint = controlPlane.Model.Url }), HttpStatusCode.Created);
        return new SlowSetupRepository(controlPlane, person, owner, workspace.Id, repository.Id, account.Id);
    }

    /// <summary>A chat whose nook starts with the repository, on the provider.</summary>
    public async Task<ChatSummary> StartChatAsync(string provider)
    {
        return await Api.ReadAsync<ChatSummary>(
            _person.SendPostAsync(
                string.Create(CultureInfo.InvariantCulture, $"/workspaces/{Workspace.Value}/chats"),
                new { provider, harness = "claude-code", account = Account, repositories = new[] { new { repository = Repository } } }),
            HttpStatusCode.Created);
    }

    /// <summary>A commit on main after the chats that came before.</summary>
    public async Task MoveOnAsync()
    {
        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal) { ["LATEST.md"] = "latest\n" };
        await _controlPlane.GitHub.CommitFilesAsync(Owner, "api", "main", files, "Move on");
    }
}
