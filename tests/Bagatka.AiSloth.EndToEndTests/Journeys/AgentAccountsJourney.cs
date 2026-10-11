using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: Alice pays for agents. Her workspace's API key is never shown again, her own accounts serve
/// only her, in every workspace, plans are personal and Claude's are off, a ChatGPT plan is added by
/// signing in; Codex and pi answer through the model gateway on an OpenAI key, Codex on a model chosen
/// from its list, one OpenRouter key runs
/// pi and Claude Code each on the model chosen from OpenRouter's catalog, and she saves that choice as a
/// preset; a ChatGPT plan is renewed one call at a time, and a plan whose sign-in ended says why.
/// </summary>
public sealed class AgentAccountsJourney(ControlPlane app) : IDisposable
{
    // Where ChatGPT sends the browser back; a client such as sloth listens there.
    private const string Callback = "http://127.0.0.1:1455/auth/callback";

    private readonly HttpClient _alice = app.ClientFor("alice-" + Guid.CreateVersion7());
    private readonly HttpClient _bob = app.ClientFor("bob-" + Guid.CreateVersion7());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task People_add_agent_accounts_and_every_kind_pays_for_agents_through_the_gateway()
    {
        await AWorkspacesKeyIsNeverShownAgainAndOnlyItsMembersSeeItAsync();
        await OwnAccountsServeTheirOwnerInEveryWorkspaceAndPlansStayPersonalAsync();
        await SlothListsEachKindByANameThatCantBeConfusedAndSavesAPresetAsync();
        await CodexAndPiAnswerThroughTheGatewayOnAnOpenAIKeyAsync();
        await OneOpenRouterKeyRunsEachAgentOnTheModelChosenForItsChatAsync();
        await ACopilotPlanRunsCopilotAndPiThroughTheGatewayAsync();
        await AChoiceOfHarnessAccountAndModelIsSavedAsAPresetAsync();
        await AChatGptPlanIsAddedBySigningInAndRenewedOneCallAtATimeAsync();
    }

    public void Dispose()
    {
        _alice.Dispose();
        _bob.Dispose();
    }

    private async Task AWorkspacesKeyIsNeverShownAgainAndOnlyItsMembersSeeItAsync()
    {
        WorkspaceSummary workspace = await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        string accounts = Paths.Workspace(workspace.Id) + "/agent-accounts";

        HttpResponseMessage added = await _alice.SendPostAsync(accounts, new { kind = "AnthropicApiKey", name = " Team key ", secret = "sk-ant-secret-value" });
        string body = await added.Content.ReadAsStringAsync(Ct);
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(added, HttpStatusCode.Created);
        Problem bobAdds = await Api.ProblemAsync(_bob.SendPostAsync(accounts, new { kind = "AnthropicApiKey", name = "Mine", secret = "sk" }), HttpStatusCode.NotFound);
        Problem withQuery = await Api.ProblemAsync(
            _alice.SendPostAsync(accounts, new { kind = "OpenAIApiKey", name = "Mine", secret = "sk", endpoint = "https://example.com/v1?key=secret" }), HttpStatusCode.BadRequest);
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(Paths.Account(account.Id), UriKind.Relative), Ct), HttpStatusCode.NoContent);
        IReadOnlyList<AgentAccountSummary> remaining = await Api.ReadAsync<IReadOnlyList<AgentAccountSummary>>(_alice.SendGetAsync(accounts), HttpStatusCode.OK);

        Assert.DoesNotContain("sk-ant-secret-value", body, StringComparison.Ordinal);
        Assert.Equal("Team key", account.Name);
        Assert.Null(account.OwnerId);
        Assert.Equal(WorkspacesErrors.NotFound.Code, bobAdds.Code);
        Assert.True(withQuery.Errors?.ContainsKey("endpoint"));
        Assert.Empty(remaining);
    }

    // An own account serves its owner in every workspace; a plan is only ever someone's own. A Copilot
    // plan is added with a code the person approves at GitHub, and lists the chat models of Copilot's
    // picker that the plan allows.
    private async Task OwnAccountsServeTheirOwnerInEveryWorkspaceAndPlansStayPersonalAsync()
    {
        WorkspaceSummary acme = await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        WorkspaceSummary personal = await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Mine" }), HttpStatusCode.Created);
        WorkspaceSummary bobs = await Api.ReadAsync<WorkspaceSummary>(_bob.SendPostAsync("/workspaces", new { name = "Bob's" }), HttpStatusCode.Created);

        SignInStarted started = await Api.ReadAsync<SignInStarted>(_alice.SendPostAsync("/agent-accounts/sign-ins", new { kind = "CopilotPlan", name = "My Copilot" }), HttpStatusCode.OK);
        SignInProgress pending = await Api.ReadAsync<SignInProgress>(_alice.SendPostAsync(Paths.SignIn(started.Id) + "/complete", new { }), HttpStatusCode.OK);
        app.GitHub.Approve(started.UserCode!, "alice-gh");
        SignInProgress approved = await Api.ReadAsync<SignInProgress>(_alice.SendPostAsync(Paths.SignIn(started.Id) + "/complete", new { }), HttpStatusCode.OK);
        AgentAccountSummary own = approved.Account!;
        Problem withToken = await Api.ProblemAsync(
            _alice.SendPostAsync("/agent-accounts", new { kind = "CopilotPlan", name = "My Copilot", secret = "github_pat_example" }), HttpStatusCode.BadRequest);
        IReadOnlyList<AgentAccountSummary> inAcme = await ListAsync(_alice, acme.Id);
        IReadOnlyList<AgentAccountSummary> inPersonal = await ListAsync(_alice, personal.Id);
        IReadOnlyList<AgentAccountSummary> inBobs = await ListAsync(_bob, bobs.Id);
        IReadOnlyList<AgentModel> copilot = await Api.ReadAsync<IReadOnlyList<AgentModel>>(
            _alice.SendGetAsync(string.Create(CultureInfo.InvariantCulture, $"{Paths.Workspace(acme.Id)}/models?harness=copilot&account={own.Id.Value}")), HttpStatusCode.OK);
        await Api.ProblemAsync(_bob.DeleteAsync(new Uri(Paths.Account(own.Id), UriKind.Relative), Ct), HttpStatusCode.NotFound);
        Problem sharedPlan = await Api.ProblemAsync(
            _alice.SendPostAsync(Paths.Workspace(acme.Id) + "/agent-accounts", new { kind = "CopilotPlan", name = "Shared", secret = "github_pat_example" }), HttpStatusCode.BadRequest);
        Problem claudePlan = await Api.ProblemAsync(
            _alice.SendPostAsync("/agent-accounts", new { kind = "ClaudePlan", name = "Mine", secret = "sk-ant-oat-example" }), HttpStatusCode.BadRequest);

        Assert.Null(pending.Account);
        Assert.Equal(("My Copilot", AgentAccountKind.CopilotPlan), (own.Name, own.Kind));
        Assert.True(withToken.Errors?.ContainsKey("kind"));
        Assert.Contains(inAcme, found => found.Id == own.Id);
        Assert.Contains(inPersonal, found => found.Id == own.Id);
        Assert.DoesNotContain(inBobs, found => found.Id == own.Id);
        Assert.Equal(["claude-opus-5.5", "gpt-6.1-sol"], copilot.Select(model => model.Id), StringComparer.Ordinal);
        Assert.True(sharedPlan.Errors?.ContainsKey("kind"));
        Assert.True(claudePlan.Errors?.ContainsKey("kind"));
    }

    // Erin finds a model by name for pi on her OpenRouter key, and saves the choice as a preset. Her
    // Copilot plan she adds with the code sloth shows, which her browser approves at GitHub.
    private async Task SlothListsEachKindByANameThatCantBeConfusedAndSavesAPresetAsync()
    {
        await using SlothCli sloth = new SlothCli("erin-" + Guid.CreateVersion7());
        await sloth.RunAsync("host", "add", app.WebApiUrl.AbsoluteUri);

        int listed = await sloth.RunAsync("account", "add");
        string kinds = sloth.Output;
        int added = await sloth.RunWithInputAsync(FakeModel.OpenRouterKey + "\n", "account", "add", "openrouter-api-key", "--name", "OpenRouter");
        int found = await sloth.RunAsync("model", "list", "luna", "--harness", "pi", "--account", "OpenRouter");
        string models = sloth.Output;
        int saved = await sloth.RunAsync("preset", "add", "--harness", "pi", "--account", "OpenRouter", "--model", FakeModel.OpenRouterModel, "--name", "Luna");
        int presetsListed = await sloth.RunAsync("preset", "list");
        string presets = sloth.Output;
        int copilotAdded = await sloth.RunAsync("account", "add", "copilot-plan");
        string copilot = sloth.Output;

        Assert.Equal((0, 0, 0, 0, 0, 0), (listed, added, found, saved, presetsListed, copilotAdded));
        Assert.Matches("copilot-plan +GitHub Copilot +sign in with GitHub", kinds);
        Assert.Matches("^Open http\\S+ and enter [0-9A-F]+ to add your Copilot plan\\.\nAdded \"Copilot plan\": yours\\.", copilot);
        Assert.Matches("chatgpt-plan +ChatGPT Plus or Pro +sign in with ChatGPT +Codex, pi", kinds);
        Assert.Matches("claude-plan +Claude Pro or Max +not allowed on ", kinds);
        Assert.Matches("anthropic-api-key +Anthropic's API.+Claude Code, pi", kinds);
        Assert.Matches("openrouter-api-key +Many vendors' models with one key +API key +Claude Code, pi", kinds);
        Assert.Matches(FakeModel.OpenRouterModel + " +OpenAI: GPT-6 Luna", models);
        Assert.DoesNotContain(FakeModel.OpenRouterClaude, models, StringComparison.Ordinal);
        Assert.Matches("Luna +pi · " + FakeModel.OpenRouterModel + " · medium · OpenRouter", presets);
    }

    // OpenAI's list of models holds every model its API serves; only those an agent can run on are
    // offered. Codex switches to one mid-chat: the next turn's agent runs on it, and remembers.
    private async Task CodexAndPiAnswerThroughTheGatewayOnAnOpenAIKeyAsync()
    {
        TestWorkspace workspace = await TestWorkspace.CreateAsync(app, _alice, openAI: true);
        IReadOnlyList<AgentModel> models = await Api.ReadAsync<IReadOnlyList<AgentModel>>(_alice.SendGetAsync(ModelsPath(workspace, "codex", workspace.Account)), HttpStatusCode.OK);
        AgentAccountSummary own = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync("/agent-accounts", new { kind = "OpenAIApiKey", name = "Mine", secret = FakeModel.ApiKey, endpoint = app.Model.OpenAIUrl }), HttpStatusCode.Created);
        ChatSummary codex = await workspace.StartChatAsync("codex", workspace.Account);
        await using ChatWatch watch = await ChatWatch.OpenAsync(workspace.Person, codex);
        Task<JsonElement> pi = TurnAsync(workspace, "pi", workspace.Account);

        await workspace.SendAsync(codex, "say hello");
        JsonElement first = await watch.NextAsync("turn-ended");
        ChatSummary switched = await Api.ReadAsync<ChatSummary>(
            _alice.SendPutAsync(Paths.Chat(codex) + "/model", new { account = workspace.Account, model = FakeModel.OpenAIModel, thinking = "high" }), HttpStatusCode.OK);
        JsonElement changed = await watch.NextAsync("model-changed");
        Problem toOwn = await Api.ProblemAsync(_alice.SendPutAsync(Paths.Chat(codex) + "/model", new { account = own.Id, model = FakeModel.OpenAIModel, thinking = "high" }), HttpStatusCode.BadRequest);
        Problem tooHard = await Api.ProblemAsync(_alice.SendPutAsync(Paths.Chat(codex) + "/model", new { account = workspace.Account, model = FakeModel.OpenAIModel, thinking = "max" }), HttpStatusCode.BadRequest);
        await workspace.SendAsync(codex, "say hello again");
        JsonElement restarted = await watch.NextAsync("agent-restarted");
        JsonElement second = await watch.NextAsync("turn-ended");
        JsonElement piEnded = await pi;

        Assert.Equal([FakeModel.OpenAIModel], models.Select(model => model.Id), StringComparer.Ordinal);
        Assert.All([first, second, piEnded], turn => Assert.Equal("end_turn", turn.GetProperty("stopReason").GetString()));
        Assert.Equal((FakeModel.OpenAIModel, "high"), (switched.Model, switched.Thinking));
        Assert.Equal(("high", FakeModel.OpenAIModel), (changed.GetProperty("thinking").GetString(), changed.GetProperty("model").GetString()));
        Assert.True(toOwn.Errors?.ContainsKey("account"));
        Assert.True(tooHard.Errors?.ContainsKey("thinking"));
        Assert.True(restarted.GetProperty("remembers").GetBoolean());
        Assert.Contains(app.Model.OpenAIAuthorizations, authorization => string.Equals(authorization, "Bearer " + FakeModel.ApiKey, StringComparison.Ordinal));
        Assert.Contains(app.Model.Requests, body => body.Contains("\"model\":\"" + FakeModel.OpenAIModel + "\"", StringComparison.Ordinal)
            && body.Contains("\"effort\":\"high\"", StringComparison.Ordinal) && body.Contains("say hello again", StringComparison.Ordinal));
    }

    // OpenRouter speaks Anthropic's API for Claude Code and OpenAI's chat API for pi, at one address and
    // with one key, under its own names for models; models that can't call tools or are only for batches
    // aren't offered.
    private async Task OneOpenRouterKeyRunsEachAgentOnTheModelChosenForItsChatAsync()
    {
        TestWorkspace workspace = await TestWorkspace.CreateAsync(app, _alice);
        AgentAccountSummary key = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync(workspace.Path + "/agent-accounts", new { kind = "OpenRouterApiKey", name = "OpenRouter", secret = FakeModel.OpenRouterKey }), HttpStatusCode.Created);
        IReadOnlyList<AgentModel> models = await Api.ReadAsync<IReadOnlyList<AgentModel>>(_alice.SendGetAsync(ModelsPath(workspace, "pi", key.Id)), HttpStatusCode.OK);
        IReadOnlyList<AgentModel> anthropic = await Api.ReadAsync<IReadOnlyList<AgentModel>>(_alice.SendGetAsync(ModelsPath(workspace, "claude-code", workspace.Account)), HttpStatusCode.OK);
        Problem codex = await Api.ProblemAsync(_alice.SendGetAsync(ModelsPath(workspace, "codex", key.Id)), HttpStatusCode.BadRequest);

        JsonElement[] ended = await Task.WhenAll(
            TurnAsync(workspace, "pi", key.Id, FakeModel.OpenRouterModel),
            TurnAsync(workspace, "claude-code", key.Id, FakeModel.OpenRouterClaude));

        Assert.Equal([FakeModel.OpenRouterModel, FakeModel.OpenRouterClaude, FakeModel.OpenAIModel], models.Select(model => model.Id), StringComparer.Ordinal);
        Assert.Contains(anthropic, model => model is { Id: FakeModel.OpenRouterClaude, Name: "Anthropic: Claude Opus 5.5" });
        Assert.True(codex.Errors?.ContainsKey("account"));
        Assert.All(ended, turn => Assert.Equal("end_turn", turn.GetProperty("stopReason").GetString()));
        Assert.Contains(app.Model.Requests, body => body.Contains("\"model\":\"" + FakeModel.OpenRouterModel + "\"", StringComparison.Ordinal) && body.Contains("\"messages\"", StringComparison.Ordinal));
        Assert.Contains(app.Model.Requests, body => body.Contains("\"model\":\"" + FakeModel.OpenRouterClaude + "\"", StringComparison.Ordinal));
        Assert.Contains(app.Model.OpenAIAuthorizations, authorization => string.Equals(authorization, "Bearer " + FakeModel.OpenRouterKey, StringComparison.Ordinal));
        Assert.Contains(app.Model.Credentials, credentials => string.Equals(credentials.Authorization, "Bearer " + FakeModel.OpenRouterKey, StringComparison.Ordinal));
    }

    // A Copilot plan's token stays on the host: Copilot and pi call the gateway with their chat's token,
    // each in the API Copilot takes for its model (Responses for one that speaks only that, OpenAI's
    // chat API otherwise), and the gateway calls Copilot with the plan's token and its integration.
    private async Task ACopilotPlanRunsCopilotAndPiThroughTheGatewayAsync()
    {
        TestWorkspace workspace = await TestWorkspace.CreateAsync(app, _alice);
        AgentAccountSummary plan = await TestWorkspace.AddCopilotPlanAsync(app, _alice, "alice-copilot-" + Guid.CreateVersion7());

        JsonElement[] ended = await Task.WhenAll(
            TurnAsync(workspace, "copilot", plan.Id, FakeModel.CopilotGpt),
            TurnAsync(workspace, "pi", plan.Id, FakeModel.CopilotClaude));

        Assert.All(ended, turn => Assert.Equal("end_turn", turn.GetProperty("stopReason").GetString()));
        Assert.Contains(app.Model.CopilotCalls, call => call is { Path: "/copilot/responses", Integration: "copilot-developer-cli" });
        Assert.Contains(app.Model.CopilotCalls, call => call is { Path: "/copilot/chat/completions", Integration: "copilot-developer-cli" });
        Assert.Contains(app.Model.Requests, body => body.Contains("\"model\":\"" + FakeModel.CopilotGpt + "\"", StringComparison.Ordinal) && body.Contains("\"effort\":\"medium\"", StringComparison.Ordinal));
    }

    // A preset is checked as a chat's start is, kept once, and only by its owner.
    private async Task AChoiceOfHarnessAccountAndModelIsSavedAsAPresetAsync()
    {
        TestWorkspace workspace = await TestWorkspace.CreateAsync(app, _alice);
        AgentAccountSummary key = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync(workspace.Path + "/agent-accounts", new { kind = "OpenRouterApiKey", name = "OpenRouter", secret = FakeModel.OpenRouterKey }), HttpStatusCode.Created);
        string presets = workspace.Path + "/presets";

        PresetSummary saved = await Api.ReadAsync<PresetSummary>(_alice.SendPostAsync(presets, new { harness = "pi", account = key.Id, model = FakeModel.OpenRouterModel, thinking = "medium" }), HttpStatusCode.Created);
        PresetSummary claude = await Api.ReadAsync<PresetSummary>(_alice.SendPostAsync(presets, new { harness = "claude-code", account = workspace.Account, model = FakeModel.OpenRouterClaude, thinking = "xhigh", name = "Claude" }), HttpStatusCode.Created);
        Problem modelless = await Api.ProblemAsync(_alice.SendPostAsync(presets, new { harness = "claude-code", account = workspace.Account, model = "", thinking = "high" }), HttpStatusCode.BadRequest);
        Problem again = await Api.ProblemAsync(_alice.SendPostAsync(presets, new { harness = "pi", account = key.Id, model = FakeModel.OpenRouterModel, thinking = "medium" }), HttpStatusCode.Conflict);
        Problem codex = await Api.ProblemAsync(_alice.SendPostAsync(presets, new { harness = "codex", account = key.Id, model = FakeModel.OpenAIModel, thinking = "medium" }), HttpStatusCode.BadRequest);
        PresetSummary renamed = await Api.ReadAsync<PresetSummary>(_alice.SendPutAsync(Paths.Preset(saved.Id) + "/name", new { name = "Luna" }), HttpStatusCode.OK);
        await Api.ProblemAsync(_bob.DeleteAsync(new Uri(Paths.Preset(saved.Id), UriKind.Relative), Ct), HttpStatusCode.NotFound);
        IReadOnlyList<PresetSummary> listed = await Api.ReadAsync<IReadOnlyList<PresetSummary>>(_alice.SendGetAsync(presets), HttpStatusCode.OK);
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(Paths.Preset(claude.Id), UriKind.Relative), Ct), HttpStatusCode.NoContent);
        IReadOnlyList<PresetSummary> left = await Api.ReadAsync<IReadOnlyList<PresetSummary>>(_alice.SendGetAsync(presets), HttpStatusCode.OK);

        Assert.Equal("pi · " + FakeModel.OpenRouterModel + " · medium", saved.Name);
        Assert.Equal(("Claude", FakeModel.OpenRouterClaude, "xhigh"), (claude.Name, claude.Model, claude.Thinking));
        Assert.True(modelless.Errors?.ContainsKey("model"));
        Assert.Equal(ChatsErrors.PresetExists.Code, again.Code);
        Assert.True(codex.Errors?.ContainsKey("account"));
        Assert.Equal(["Luna", "Claude"], listed.Select(preset => preset.Name), StringComparer.Ordinal);
        Assert.Equal([renamed], left);
    }

    // Every token set the fake ChatGPT issues asks to be renewed at once, so each model call renews:
    // two agents working at the same time on one plan must never use a refresh token twice.
    private async Task AChatGptPlanIsAddedBySigningInAndRenewedOneCallAtATimeAsync()
    {
        TestWorkspace workspace = await TestWorkspace.CreateAsync(app, _alice, openAI: true);
        SignInStarted declinedStart = await StartSignInAsync();
        string state = QueryHelpers.ParseQuery(declinedStart.Url.Query)["state"].ToString();
        Problem declined = await Api.ProblemAsync(
            _alice.SendPostAsync(CompletePath(declinedStart), new { returnedTo = Callback + "?error=access_denied&state=" + state }), HttpStatusCode.BadRequest);
        SignInStarted started = await StartSignInAsync();
        Uri returnedTo = await FakeChatGpt.FollowAsync(started.Url);
        SignInProgress signedIn = await Api.ReadAsync<SignInProgress>(_alice.SendPostAsync(CompletePath(started), new { returnedTo }), HttpStatusCode.OK);
        AgentAccountSummary plan = signedIn.Account!;
        Problem again = await Api.ProblemAsync(_alice.SendPostAsync(CompletePath(started), new { returnedTo }), HttpStatusCode.NotFound);
        int renewalsBefore = app.ChatGpt.Renewals;

        IReadOnlyList<AgentModel> planModels = await Api.ReadAsync<IReadOnlyList<AgentModel>>(_alice.SendGetAsync(ModelsPath(workspace, "codex", plan.Id)), HttpStatusCode.OK);
        JsonElement[] ended = await Task.WhenAll(TurnAsync(workspace, "codex", plan.Id), TurnAsync(workspace, "codex", plan.Id));
        ChatSummary later = await workspace.StartChatAsync("codex", plan.Id);
        await using ChatWatch laterWatch = await ChatWatch.OpenAsync(workspace.Person, later);
        app.ChatGpt.Disconnect(QueryHelpers.ParseQuery(returnedTo.Query)["client_id"].ToString());
        await workspace.SendAsync(later, "say hello");
        JsonElement afterDisconnect = await laterWatch.NextAsync("turn-ended");
        IReadOnlyList<AgentAccountSummary> accounts = await ListAsync(_alice, workspace.Id);

        Assert.Equal("The sign-in was declined.", declined.Errors?.GetValueOrDefault("returnedTo")?.Single());
        Assert.Equal((AgentAccountKind.ChatGptPlan, false), (plan.Kind, plan.NeedsSignIn));
        Assert.Equal(AgentAccountsErrors.SignInNotFound.Code, again.Code);
        Assert.Equal([("gpt-6.1-sol", "GPT-6.1 Sol"), (FakeModel.OpenAIModel, "GPT-5.6 Terra")], planModels.Select(model => (model.Id, model.Name)));
        Assert.All(ended, turn => Assert.Equal("end_turn", turn.GetProperty("stopReason").GetString()));
        Assert.True(app.ChatGpt.Renewals > renewalsBefore);
        Assert.Equal(0, app.ChatGpt.ReusedRefreshTokens);
        Assert.Equal("The agent couldn't start: " + AgentAccountsErrors.SignInEnded.Message, afterDisconnect.GetProperty("failure").GetString());
        Assert.True(accounts.Single(found => found.Id == plan.Id).NeedsSignIn);
    }

    private async Task<SignInStarted> StartSignInAsync()
    {
        return await Api.ReadAsync<SignInStarted>(
            _alice.SendPostAsync("/agent-accounts/sign-ins", new { kind = "ChatGptPlan", name = "My ChatGPT", callback = Callback }), HttpStatusCode.OK);
    }

    private static string CompletePath(SignInStarted started)
    {
        return Paths.SignIn(started.Id) + "/complete";
    }

    private static async Task<IReadOnlyList<AgentAccountSummary>> ListAsync(HttpClient client, WorkspaceId workspace)
    {
        return await Api.ReadAsync<IReadOnlyList<AgentAccountSummary>>(client.SendGetAsync(Paths.Workspace(workspace) + "/agent-accounts"), HttpStatusCode.OK);
    }

    private static string ModelsPath(TestWorkspace workspace, string harness, AgentAccountId account)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{workspace.Path}/models?harness={harness}&account={account.Value}");
    }

    // A chat of the harness on the account, on the model when given, and how its first turn ended.
    private static async Task<JsonElement> TurnAsync(TestWorkspace workspace, string harness, AgentAccountId account, string? model = null)
    {
        ChatSummary chat = await workspace.StartChatAsync(harness, account, model: model);
        await using ChatWatch watch = await ChatWatch.OpenAsync(workspace.Person, chat);
        await workspace.SendAsync(chat, "say hello");
        return await watch.NextAsync("turn-ended");
    }
}
