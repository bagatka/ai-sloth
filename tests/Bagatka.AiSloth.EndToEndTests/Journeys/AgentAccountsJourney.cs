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
using Bagatka.Foundation;
using Xunit;
using Bagatka.ModelAccess;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: Alice pays for agents. Her workspace's API key is never shown again, her own accounts serve
/// only her, in every workspace, and plans are personal; Codex and pi answer through the model gateway
/// on an OpenAI key, Codex on a model chosen from its list, one OpenRouter key runs pi and Claude Code
/// each on the model chosen from OpenRouter's catalog, and she saves that choice as a preset. Her
/// Claude and ChatGPT plans sign in as Claude Code and Codex do, run them through the gateway, renewed
/// one call at a time, and show their limits; a plan whose sign-in ended says why.
/// </summary>
public sealed class AgentAccountsJourney(ControlPlane app) : IDisposable
{
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
        await AClaudePlanRunsClaudeCodeThroughTheGatewayAndShowsItsLimitsAsync();
        await AChatGptPlanRunsCodexOnChatGptsBackendAndShowsItsLimitsAsync();
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
    // plan is added with a code the person approves at GitHub, lists the chat models of Copilot's
    // picker that the plan allows, and says how many of its premium requests are used.
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
        IReadOnlyList<HarnessModel> copilot = await Api.ReadAsync<IReadOnlyList<HarnessModel>>(
            _alice.SendGetAsync(string.Create(CultureInfo.InvariantCulture, $"{Paths.Workspace(acme.Id)}/models?harness=copilot&account={own.Id.Value}")), HttpStatusCode.OK);
        await Api.ProblemAsync(_bob.DeleteAsync(new Uri(Paths.Account(own.Id), UriKind.Relative), Ct), HttpStatusCode.NotFound);
        Problem sharedPlan = await Api.ProblemAsync(
            _alice.SendPostAsync(Paths.Workspace(acme.Id) + "/agent-accounts", new { kind = "CopilotPlan", name = "Shared", secret = "github_pat_example" }), HttpStatusCode.BadRequest);
        Problem claudePlan = await Api.ProblemAsync(
            _alice.SendPostAsync("/agent-accounts", new { kind = "ClaudePlan", name = "Mine", secret = "sk-ant-oat-example" }), HttpStatusCode.BadRequest);
        PlanUsage usage = await Api.ReadAsync<PlanUsage>(_alice.SendGetAsync(UsagePath(acme.Id, own.Id)), HttpStatusCode.OK);
        await Api.ProblemAsync(_bob.SendGetAsync(UsagePath(bobs.Id, own.Id)), HttpStatusCode.NotFound);

        Assert.Null(pending.Account);
        Assert.Equal(("My Copilot", ModelAccessKind.CopilotPlan), (own.Name, own.Kind));
        Assert.True(withToken.Errors?.ContainsKey("kind"));
        Assert.Contains(inAcme, found => found.Id == own.Id);
        Assert.Contains(inPersonal, found => found.Id == own.Id);
        Assert.DoesNotContain(inBobs, found => found.Id == own.Id);
        Assert.Equal(["claude-opus-5.5", "gpt-6.1-sol", "gpt-4.1"], copilot.Select(model => model.Id), StringComparer.Ordinal);
        Assert.Equal(["none", "minimal", "low", "medium", "high", "xhigh", "max"], copilot[0].ThinkingLevels);
        Assert.Equal(["low", "medium", "high"], copilot[1].ThinkingLevels);
        Assert.Empty(copilot[2].ThinkingLevels);
        Assert.Equal((200_000, 4m, true), (copilot[0].Facts.ContextWindow, copilot[0].Facts.Price?.Input, copilot[0].Facts.Price?.Estimate));
        Assert.True(sharedPlan.Errors?.ContainsKey("kind"));
        Assert.True(claudePlan.Errors?.ContainsKey("kind"));
        Assert.Equal([("Premium requests", 12m, (decimal?)300m, "requests")], usage.Meters.Select(meter => (meter.Name, meter.Used, meter.Limit, meter.Unit)));
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
        Assert.Matches("(?m)chatgpt-plan +ChatGPT Plus or Pro +sign in with OpenAI +Codex$", kinds);
        Assert.Matches("(?m)claude-plan +Claude Pro or Max +sign in with Claude +Claude Code$", kinds);
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
        IReadOnlyList<HarnessModel> models = await Api.ReadAsync<IReadOnlyList<HarnessModel>>(_alice.SendGetAsync(ModelsPath(workspace, "codex", workspace.Account)), HttpStatusCode.OK);
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
    // aren't offered. Each of pi's calls costs GPT-6 Luna's price: 10 tokens in at $0.10 a million and 3
    // out at $0.50, which the chat's list shows, and the workspace's usage by day: today's where the
    // person is, here fourteen hours ahead of UTC.
    private async Task OneOpenRouterKeyRunsEachAgentOnTheModelChosenForItsChatAsync()
    {
        TestWorkspace workspace = await TestWorkspace.CreateAsync(app, _alice);
        AgentAccountSummary key = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync(workspace.Path + "/agent-accounts", new { kind = "OpenRouterApiKey", name = "OpenRouter", secret = FakeModel.OpenRouterKey }), HttpStatusCode.Created);
        IReadOnlyList<HarnessModel> models = await Api.ReadAsync<IReadOnlyList<HarnessModel>>(_alice.SendGetAsync(ModelsPath(workspace, "pi", key.Id)), HttpStatusCode.OK);
        IReadOnlyList<HarnessModel> anthropic = await Api.ReadAsync<IReadOnlyList<HarnessModel>>(_alice.SendGetAsync(ModelsPath(workspace, "claude-code", workspace.Account)), HttpStatusCode.OK);
        Problem codex = await Api.ProblemAsync(_alice.SendGetAsync(ModelsPath(workspace, "codex", key.Id)), HttpStatusCode.BadRequest);
        DateTimeOffset before = TimeProvider.System.GetUtcNow();

        JsonElement[] ended = await Task.WhenAll(
            TurnAsync(workspace, "pi", key.Id, FakeModel.OpenRouterModel),
            TurnAsync(workspace, "claude-code", key.Id, FakeModel.OpenRouterClaude));
        Page<ChatSummary> chats = await Api.ReadAsync<Page<ChatSummary>>(_alice.SendGetAsync(workspace.Path + "/chats"), HttpStatusCode.OK);
        ChatSummary piChat = chats.Items.Single(chat => string.Equals(chat.Harness, "pi", StringComparison.Ordinal));
        ChatUsage piUsage = await Api.EventuallyAsync(async () =>
        {
            ChatUsage read = await Api.ReadAsync<ChatUsage>(_alice.SendGetAsync(Paths.Chat(piChat) + "/usage"), HttpStatusCode.OK);
            return read.Calls > 0 && read.Turns.Sum(turn => turn.Calls) == read.Calls ? read : null;
        });
        WorkspaceUsage week = await Api.ReadAsync<WorkspaceUsage>(_alice.SendGetAsync(workspace.Path + "/usage?days=7&timeZone=Pacific/Kiritimati"), HttpStatusCode.OK);
        Problem nowhere = await Api.ProblemAsync(_alice.SendGetAsync(workspace.Path + "/usage?days=7&timeZone=Mars/Olympus"), HttpStatusCode.BadRequest);
        (DateOnly first, DateOnly last) = (Kiritimati(before), Kiritimati(TimeProvider.System.GetUtcNow()));

        Assert.Equal([FakeModel.OpenRouterModel, FakeModel.OpenRouterClaude, FakeModel.OpenAIModel], models.Select(model => model.Id), StringComparer.Ordinal);
        Assert.Equal(["off", "low", "medium", "high", "xhigh", "max"], models[0].ThinkingLevels);
        Assert.Equal((400_000, 0.1m, 0.5m, false), (models[0].Facts.ContextWindow, models[0].Facts.Price?.Input, models[0].Facts.Price?.Output, models[0].Facts.Price?.Estimate));
        Assert.Equal([272_000], models[0].Facts.Price?.Tiers.Select(tier => tier.AboveInputTokens));
        Assert.Contains(anthropic, model => model is { Id: FakeModel.OpenRouterClaude, Name: "Anthropic: Claude Opus 5.5" });
        Assert.Equal((piUsage.Calls * 0.0000025m, false), (piUsage.Cost, piUsage.Estimate));
        Assert.Equal(7, week.Days.Count);
        Assert.InRange(week.Days[^1].Day, first, last);
        Assert.True(week.Days.Where(day => day.Day >= first && day.Day <= last).Sum(day => day.Calls) >= piUsage.Calls);
        Assert.Equal(week.Calls, week.Days.Sum(day => day.Calls));
        Assert.Equal(piUsage.Cost, week.Chats.Single(chat => chat.Chat == piChat.Id).Cost);
        Assert.True(nowhere.Errors?.ContainsKey("timeZone"));
        Assert.True(codex.Errors?.ContainsKey("account"));
        Assert.All(ended, turn => Assert.Equal("end_turn", turn.GetProperty("stopReason").GetString()));
        Assert.Contains(app.Model.Requests, body => body.Contains("\"model\":\"" + FakeModel.OpenRouterModel + "\"", StringComparison.Ordinal) && body.Contains("\"messages\"", StringComparison.Ordinal));
        Assert.Contains(app.Model.Requests, body => body.Contains("\"model\":\"" + FakeModel.OpenRouterClaude + "\"", StringComparison.Ordinal));
        Assert.Contains(app.Model.OpenAIAuthorizations, authorization => string.Equals(authorization, "Bearer " + FakeModel.OpenRouterKey, StringComparison.Ordinal));
        Assert.Contains(app.Model.Credentials, credentials => string.Equals(credentials.Authorization, "Bearer " + FakeModel.OpenRouterKey, StringComparison.Ordinal));
    }

    // A Copilot plan's token stays on the host: Copilot and pi call the gateway with their chat's token,
    // each in the API Copilot takes for its model (Responses for one that speaks only that, OpenAI's
    // chat API otherwise), and the gateway calls Copilot with the plan's token and its integration. A
    // plan billed for what it uses, as an enterprise's, reports the credits it spent.
    private async Task ACopilotPlanRunsCopilotAndPiThroughTheGatewayAsync()
    {
        TestWorkspace workspace = await TestWorkspace.CreateAsync(app, _alice);
        string login = "alice-copilot-" + Guid.CreateVersion7();
        AgentAccountSummary plan = await TestWorkspace.AddCopilotPlanAsync(app, _alice, login);
        app.GitHub.BillCopilotCredits(login);

        JsonElement[] ended = await Task.WhenAll(
            TurnAsync(workspace, "copilot", plan.Id, FakeModel.CopilotGpt),
            TurnAsync(workspace, "pi", plan.Id, FakeModel.CopilotClaude));
        PlanUsage usage = await Api.ReadAsync<PlanUsage>(_alice.SendGetAsync(UsagePath(workspace.Id, plan.Id)), HttpStatusCode.OK);

        Assert.All(ended, turn => Assert.Equal("end_turn", turn.GetProperty("stopReason").GetString()));
        Assert.Contains(app.Model.CopilotCalls, call => call is { Path: "/copilot/responses", Integration: "copilot-developer-cli" });
        Assert.Contains(app.Model.CopilotCalls, call => call is { Path: "/copilot/chat/completions", Integration: "copilot-developer-cli" });
        Assert.Contains(app.Model.Requests, body => body.Contains("\"model\":\"" + FakeModel.CopilotGpt + "\"", StringComparison.Ordinal) && body.Contains("\"effort\":\"medium\"", StringComparison.Ordinal));
        Assert.Equal([("Credits", 41.5m, (decimal?)null, "credits")], usage.Meters.Select(meter => (meter.Name, meter.Used, meter.Limit, meter.Unit)));
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

    // A Claude plan signed in as Claude Code: Alice approves at Claude and pastes back the code its page
    // shows. Claude Code calls Anthropic's API through the gateway with the plan's token, renewed one call
    // at a time, and its calls are recorded; pi can't use it, and Anthropic says how much of the plan's
    // five-hour and weekly windows is used. Once she signs out of Claude Code everywhere, the plan says
    // why it runs no more agents.
    private async Task AClaudePlanRunsClaudeCodeThroughTheGatewayAndShowsItsLimitsAsync()
    {
        TestWorkspace workspace = await TestWorkspace.CreateAsync(app, _alice);
        string person = "alice-claude-" + Guid.CreateVersion7();
        SignInStarted started = await Api.ReadAsync<SignInStarted>(_alice.SendPostAsync("/agent-accounts/sign-ins", new { kind = "ClaudePlan", name = "My Claude" }), HttpStatusCode.OK);
        string shown = await FakeClaude.ApproveAsync(started.Url, person);
        Problem another = await Api.ProblemAsync(_alice.SendPostAsync(CompletePath(started), new { code = shown.Split('#')[0] + "#another-sign-in" }), HttpStatusCode.BadRequest);
        SignInProgress signedIn = await Api.ReadAsync<SignInProgress>(_alice.SendPostAsync(CompletePath(started), new { code = shown }), HttpStatusCode.OK);
        AgentAccountSummary plan = signedIn.Account!;
        Problem again = await Api.ProblemAsync(_alice.SendPostAsync(CompletePath(started), new { code = shown }), HttpStatusCode.NotFound);
        Problem pi = await Api.ProblemAsync(_alice.SendGetAsync(ModelsPath(workspace, "pi", plan.Id)), HttpStatusCode.BadRequest);
        int renewalsBefore = app.Claude.Renewals;

        JsonElement[] ended = await Task.WhenAll(TurnAsync(workspace, "claude-code", plan.Id), TurnAsync(workspace, "claude-code", plan.Id));
        Page<ChatSummary> chats = await Api.ReadAsync<Page<ChatSummary>>(_alice.SendGetAsync(workspace.Path + "/chats"), HttpStatusCode.OK);
        ChatUsage recorded = await Api.EventuallyAsync(async () =>
        {
            ChatUsage read = await Api.ReadAsync<ChatUsage>(_alice.SendGetAsync(Paths.Chat(chats.Items[0]) + "/usage"), HttpStatusCode.OK);
            return read.Calls > 0 ? read : null;
        });
        PlanUsage usage = await Api.ReadAsync<PlanUsage>(_alice.SendGetAsync(UsagePath(workspace.Id, plan.Id)), HttpStatusCode.OK);
        ChatSummary later = await workspace.StartChatAsync("claude-code", plan.Id);
        await using ChatWatch laterWatch = await ChatWatch.OpenAsync(workspace.Person, later);
        app.Claude.SignOut(person);
        await workspace.SendAsync(later, "say hello");
        JsonElement afterSignOut = await laterWatch.NextAsync("turn-ended");
        IReadOnlyList<AgentAccountSummary> accounts = await ListAsync(_alice, workspace.Id);

        Assert.Equal("This code belongs to another sign-in; start again.", another.Errors?.GetValueOrDefault("code")?.Single());
        Assert.Equal((ModelAccessKind.ClaudePlan, "My Claude", false), (plan.Kind, plan.Name, plan.NeedsSignIn));
        Assert.Equal(AgentAccountsErrors.SignInNotFound.Code, again.Code);
        Assert.True(pi.Errors?.ContainsKey("account"));
        Assert.All(ended, turn => Assert.Equal("end_turn", turn.GetProperty("stopReason").GetString()));
        Assert.Contains("/v1/messages", app.Model.ClaudePlanCalls, StringComparer.Ordinal);
        Assert.True(recorded.Calls > 0);
        Assert.True(app.Claude.Renewals > renewalsBefore);
        Assert.Equal(0, app.Claude.ReusedRefreshTokens);
        Assert.Equal([("5 hours", 37m, 100m), ("Week", 8m, 100m)], usage.Meters.Select(meter => (meter.Name, meter.Used, meter.Limit ?? 0)));
        Assert.Equal("The agent couldn't start: " + AgentAccountsErrors.SignInEnded.Message, afterSignOut.GetProperty("failure").GetString());
        Assert.True(accounts.Single(found => found.Id == plan.Id).NeedsSignIn);
    }

    // A ChatGPT plan signed in as Codex: a code the person enters at OpenAI. Codex's calls reach ChatGPT's
    // Codex backend with the plan's token and its ChatGPT account, the token renewed one call at a
    // time; its models are the backend's, pi can't use it, and ChatGPT says how much of the plan's
    // five-hour and weekly windows is used.
    private async Task AChatGptPlanRunsCodexOnChatGptsBackendAndShowsItsLimitsAsync()
    {
        TestWorkspace workspace = await TestWorkspace.CreateAsync(app, _alice);
        string subject = "alice-codex-" + Guid.CreateVersion7();
        SignInStarted started = await Api.ReadAsync<SignInStarted>(_alice.SendPostAsync("/agent-accounts/sign-ins", new { kind = "ChatGptPlan", name = "My ChatGPT" }), HttpStatusCode.OK);
        SignInProgress pending = await Api.ReadAsync<SignInProgress>(_alice.SendPostAsync(CompletePath(started), new { }), HttpStatusCode.OK);
        app.ChatGpt.ApproveCodex(started.UserCode!, subject);
        SignInProgress approved = await Api.EventuallyAsync(async () =>
        {
            SignInProgress progress = await Api.ReadAsync<SignInProgress>(_alice.SendPostAsync(CompletePath(started), new { }), HttpStatusCode.OK);
            return progress.Account is null ? null : progress;
        });
        AgentAccountSummary plan = approved.Account!;
        IReadOnlyList<HarnessModel> models = await Api.ReadAsync<IReadOnlyList<HarnessModel>>(_alice.SendGetAsync(ModelsPath(workspace, "codex", plan.Id)), HttpStatusCode.OK);
        Problem pi = await Api.ProblemAsync(_alice.SendGetAsync(ModelsPath(workspace, "pi", plan.Id)), HttpStatusCode.BadRequest);
        int renewalsBefore = app.ChatGpt.CodexRenewals;

        JsonElement[] ended = await Task.WhenAll(TurnAsync(workspace, "codex", plan.Id, FakeModel.CodexModel), TurnAsync(workspace, "codex", plan.Id, FakeModel.CodexModel));
        PlanUsage usage = await Api.ReadAsync<PlanUsage>(_alice.SendGetAsync(UsagePath(workspace.Id, plan.Id)), HttpStatusCode.OK);

        Assert.Null(pending.Account);
        Assert.Equal((ModelAccessKind.ChatGptPlan, "My ChatGPT"), (plan.Kind, plan.Name));
        Assert.Equal([(FakeModel.CodexModel, "GPT-6.1 Sol")], models.Select(model => (model.Id, model.Name)));
        Assert.Equal(["low", "medium", "high", "xhigh"], models[0].ThinkingLevels);
        Assert.Equal(272_000, models[0].Facts.ContextWindow);
        Assert.True(pi.Errors?.ContainsKey("account"));
        Assert.All(ended, turn => Assert.Equal("end_turn", turn.GetProperty("stopReason").GetString()));
        Assert.Contains(app.Model.CodexCalls, call => string.Equals(call.Path, "/backend-api/codex/responses", StringComparison.Ordinal) && string.Equals(call.Account, FakeChatGpt.CodexAccountOf(subject), StringComparison.Ordinal));
        Assert.True(app.ChatGpt.CodexRenewals > renewalsBefore);
        Assert.Equal(0, app.ChatGpt.CodexReusedRefreshTokens);
        Assert.Equal([("5 hours", 42m, 100m), ("Week", 12m, 100m)], usage.Meters.Select(meter => (meter.Name, meter.Used, meter.Limit ?? 0)));
    }

    private static string CompletePath(SignInStarted started)
    {
        return Paths.SignIn(started.Id) + "/complete";
    }

    private static async Task<IReadOnlyList<AgentAccountSummary>> ListAsync(HttpClient client, WorkspaceId workspace)
    {
        return await Api.ReadAsync<IReadOnlyList<AgentAccountSummary>>(client.SendGetAsync(Paths.Workspace(workspace) + "/agent-accounts"), HttpStatusCode.OK);
    }

    private static DateOnly Kiritimati(DateTimeOffset at)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati")).DateTime);
    }

    private static string UsagePath(WorkspaceId workspace, AgentAccountId account)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{Paths.Workspace(workspace)}/agent-accounts/{account.Value}/usage");
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
