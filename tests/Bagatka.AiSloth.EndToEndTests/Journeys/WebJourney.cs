using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.Foundation;
using Microsoft.Playwright;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: someone starts, follows, and steers chats in the web app the host serves. They sign in with
/// a link code from another of their devices, where their own workspace has no agent account to start
/// a chat on, find a chat in Acme, and watch its turn as it happens; a chat started elsewhere shows up
/// at once. A message they write starts the next turn, one written while the agent works goes into
/// that turn, and Esc stops a turn. They start a chat in the app with Acme's repository, choosing its
/// agent and its model, switch the chat to another model, see that the repository's setup failed and
/// fix it in a chat of its own, which runs the setup again and offers its fix as a pull request, then
/// bring the fix into the chat, review what changed in the repository, the fix too, download its files,
/// and open a pull request. The app
/// runs under the host's content security policy without a violation, and its session is a cookie no
/// script can read. On another computer, a link to the chat signs them in through the host's provider
/// and opens it. Only the model and GitHub are fake; the app is src/Apps/Web as last built, and
/// the browser is Chromium, Playwright's or the one <c>BAGATKA_CHROMIUM</c> names.
/// </summary>
public sealed partial class WebJourney(ControlPlane app)
{
    // What the model holds on in this journey; ChatJourney, on the same host, holds on its own.
    private const string Wait = "wait a moment";

    // How a chat that fixes the setup of Acme's repository asks its agent, and its title; the model
    // holds on it while the journey fixes the setup.
    private const string FixRequest = "Fix api's setup";

    [Fact]
    public async Task Someone_starts_follows_and_steers_chats_in_the_web_app()
    {
        string alice = "alice-" + Guid.CreateVersion7();
        using HttpClient aliceApi = app.ClientFor(alice);
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, aliceApi);
        string owner = await ConnectsGitHubAndAddsARepositoryAsync(aliceApi, acme);
        ChatSummary chat = await acme.StartChatAsync();
        await acme.SendAsync(chat, "Please write hello.txt for me");
        LinkCode code = await Api.ReadAsync<LinkCode>(aliceApi.SendPostAsync("/users/me/link-codes", new { }), HttpStatusCode.OK);

        await using WebBrowser browser = await WebBrowser.LaunchAsync();
        await using IBrowserContext context = await browser.NewContextAsync();
        IPage page = await context.NewPageAsync();

        try
        {
            await SignsInWithALinkCodeAsync(page, code.Code);
            await AWorkspaceWithoutAnAccountSaysWhatChatsNeedAsync(page);
            await FindsTheChatAndSeesItsTurnAsync(page);
            await AChatStartedElsewhereShowsUpAtOnceAsync(page, acme);
            await AMessageStartsTheNextTurnAsync(page);
            await AMessageCopiesAsItsTextAsync(page, context);
            await AMessageWhileTheAgentWorksGoesIntoItsTurnAsync(page);
            await EscStopsTheTurnAsync(page);
            await TheTranscriptFollowsOnlyWhileAtItsEndAsync(page, acme, chat);
            ChatSummary started = await StartsAChatWithTheRepositoryInTheAppAsync(page, acme);
            await AFailedSetupIsFixedInAChatOfItsOwnAsync(page, acme);
            await TheChangesStayLiveAndKeepTheReadersPlaceAsync(page, acme, started);
            await ReviewsTheChangesDownloadsTheFilesAndOpensAPullRequestAsync(page, acme, started, owner);
            await TheSessionIsACookieNoScriptReadsAndNothingBrokeThePolicyAsync(page, context);
            await ALinkToTheChatSignsInThroughTheProviderAsync(browser, chat, alice);
        }
        finally
        {
            // What the page looked like at the end, or where it failed, in both themes.
            await WebBrowser.ShotAsync(page, "end");
            await page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = ColorScheme.Dark });
            await WebBrowser.ShotAsync(page, "end-dark");
            app.Model.ReleaseForGood(Wait);
            app.Model.ReleaseForGood(FixRequest);
        }
    }

    private async Task SignsInWithALinkCodeAsync(IPage page, string code)
    {
        IResponse? response = await page.GotoAsync(new Uri(app.WebApiUrl, "app/").AbsoluteUri);
        Assert.True(response is { Ok: true }, "The host serves no web app: build it first, with npm run build in src/Apps/Web.");
        await Assertions.Expect(page).ToHaveURLAsync(SignInPage());

        await page.GetByLabel("Code").FillAsync(code);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in", Exact = true }).ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Searchbox).Or(page.GetByLabel("Search chats"))).ToBeVisibleAsync(WebBrowser.Shown);
    }

    // The app opens with the person's own workspace, where they added no agent account.
    private static async Task AWorkspaceWithoutAnAccountSaysWhatChatsNeedAsync(IPage page)
    {
        await Assertions.Expect(page.GetByText("No agent account yet")).ToBeVisibleAsync(WebBrowser.Shown);
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send" })).ToBeDisabledAsync();
        await WebBrowser.ShotAsync(page, "no-account");
    }

    // The chat is in Acme.
    private static async Task FindsTheChatAndSeesItsTurnAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "alice" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = "Acme" }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Please write hello.txt for me" }).ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Please write hello.txt for me" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Write").First).ToBeVisibleAsync(WebBrowser.Shown);
        await Assertions.Expect(page.GetByText("Done.")).ToBeVisibleAsync(WebBrowser.Shown);
        await Assertions.Expect(page.GetByText(TurnEnded()).First).ToBeVisibleAsync(WebBrowser.Shown);
        await WebBrowser.ShotAsync(page, "first-turn");
    }

    // Someone starts a chat from sloth or another browser: the list shows it without a reload.
    private static async Task AChatStartedElsewhereShowsUpAtOnceAsync(IPage page, TestWorkspace acme)
    {
        ChatSummary elsewhere = await acme.StartChatAsync();
        await acme.SendAsync(elsewhere, "hello from the terminal");

        await Assertions.Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "hello from the terminal" })).ToBeVisibleAsync(WebBrowser.Shown);
    }

    // With sloth or, later, the app: their GitHub account, and Acme's repository from it. Returns the
    // repository's owner on GitHub.
    private async Task<string> ConnectsGitHubAndAddsARepositoryAsync(HttpClient person, TestWorkspace acme)
    {
        string suffix = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)[^8..];
        string login = "alice-" + suffix;
        string owner = "acme-" + suffix;
        await app.GitHub.CreateRepositoryAsync(owner, "api", login);
        Dictionary<string, string> setup = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [".agents/setup"] = "#!/bin/sh\necho 'npm ERR! 404 Not Found: @acme/ui'\nexit 2\n",
        };
        await app.GitHub.CommitFilesAsync(owner, "api", "main", setup, "Add a setup");
        GitHubConnectionStarted connecting = await Api.ReadAsync<GitHubConnectionStarted>(person.SendPostAsync("/github/connections", new { }), HttpStatusCode.OK);
        app.GitHub.Approve(connecting.UserCode, login);
        await Api.ExpectAsync(person.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/github/connections/{connecting.Id.Value}/complete"), new { }), HttpStatusCode.OK);
        await Api.ExpectAsync(person.SendPostAsync(acme.Path + "/repositories", new { fullName = owner + "/api" }), HttpStatusCode.Created);
        return owner;
    }

    // The agent picker offers the harnesses the accounts run, the model picker every model of their
    // accounts, and the repository picker the workspace's repositories; the nook starts as the message
    // is written. In the chat, the model picker switches its model for the next turn.
    private static async Task<ChatSummary> StartsAChatWithTheRepositoryInTheAppAsync(IPage page, TestWorkspace acme)
    {
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "New chat" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "What should Claude Code work on?" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Agent" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitemradio, new PageGetByRoleOptions { Name = "Claude Code" }).ClickAsync();
        await page.GetByRole(AriaRole.Combobox, new PageGetByRoleOptions { Name = "Model", Exact = true }).ClickAsync();
        await page.GetByLabel("Search models").FillAsync("opus");
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "Anthropic: Claude Opus 5.5" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Combobox, new PageGetByRoleOptions { Name = "Model", Exact = true })).ToContainTextAsync("claude-opus-5.5");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "No repository" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitemcheckbox, new PageGetByRoleOptions { Name = "api" }).ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "api" })).ToBeVisibleAsync();
        await WebBrowser.ShotAsync(page, "new-chat");

        await Composer(page).FillAsync("Please write hello.txt in a new chat");
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Please write hello.txt in a new chat" })).ToBeVisibleAsync(WebBrowser.Shown);
        await Assertions.Expect(page.GetByText("Done.")).ToBeVisibleAsync(WebBrowser.Shown);
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Please write hello.txt in a new chat" })).ToBeVisibleAsync();
        await WebBrowser.ShotAsync(page, "started");

        await page.GetByRole(AriaRole.Combobox, new PageGetByRoleOptions { Name = "Model", Exact = true }).ClickAsync();
        await WebBrowser.ShotAsync(page, "model-picker");
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "Default" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Switched to the agent's default model.")).ToBeVisibleAsync(WebBrowser.Shown);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Thinking" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitemradio, new PageGetByRoleOptions { Name = "Extra high" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Switched to the agent's default model, thinking extra high.")).ToBeVisibleAsync(WebBrowser.Shown);
        Page<ChatSummary> chats = await Api.ReadAsync<Page<ChatSummary>>(acme.Person.SendGetAsync(acme.Path + "/chats"), HttpStatusCode.OK);
        Assert.Equal((null, "xhigh"), (chats.Items[0].Model, chats.Items[0].Thinking));
        return chats.Items[0];
    }

    // The repository's setup failed before the agent started: the transcript says so in a line, and the
    // message box what that means, with its output. One click starts a chat that fixes it, and the chat
    // goes on as it is, following it. There the agent fixes it (here the journey does, as it would),
    // the setup runs again, and the fix is offered as a pull request. Back in the chat, they bring the
    // fix in, and the setup runs again there too.
    private async Task AFailedSetupIsFixedInAChatOfItsOwnAsync(IPage page, TestWorkspace acme)
    {
        ILocator failed = page.GetByRole(AriaRole.Alert).Filter(new LocatorFilterOptions { HasText = "api's setup failed" });
        await Assertions.Expect(failed).ToBeVisibleAsync(WebBrowser.Shown);
        await Assertions.Expect(page.GetByText("The setup failed (exit code 2)")).ToBeVisibleAsync();
        await failed.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Output" }).ClickAsync();
        await Assertions.Expect(failed.GetByText("npm ERR! 404 Not Found: @acme/ui")).ToBeVisibleAsync();
        await WebBrowser.ShotAsync(page, "setup-failed");

        app.Model.HoldOn(FixRequest);
        await failed.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Fix the setup" }).ClickAsync();
        ILocator elsewhere = page.GetByRole(AriaRole.Alert).Filter(new LocatorFilterOptions { HasText = "api's setup is being fixed in another chat" });
        await Assertions.Expect(elsewhere).ToBeVisibleAsync(WebBrowser.Shown);
        await Assertions.Expect(page.GetByRole(AriaRole.Main).GetByText(FixRequest)).ToHaveCountAsync(0);
        await WebBrowser.ShotAsync(page, "setup-fixed-elsewhere");
        await elsewhere.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Open it" }).ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = FixRequest })).ToBeVisibleAsync(WebBrowser.Shown);
        await app.Model.HeldAsync(FixRequest);
        await Assertions.Expect(page.GetByText("Fixing api's setup. AiSloth then runs it again to check the fix.")).ToBeVisibleAsync(WebBrowser.Shown);
        await WebBrowser.ShotAsync(page, "setup-fixing");
        Page<ChatSummary> chats = await Api.ReadAsync<Page<ChatSummary>>(acme.Person.SendGetAsync(acme.Path + "/chats"), HttpStatusCode.OK);
        ChatSummary fixing = Assert.Single(chats.Items, found => string.Equals(found.Title, FixRequest, StringComparison.Ordinal));
        await acme.ChangeAsync(fixing.NookId, "printf '#!/bin/sh\\necho installed\\n' > /work/api/.agents/setup");
        app.Model.Release(FixRequest);

        await Assertions.Expect(page.GetByText("Set up again in ", new PageGetByTextOptions { Exact = false })).ToBeVisibleAsync(WebBrowser.Shown);
        ILocator works = page.GetByRole(AriaRole.Alert).Filter(new LocatorFilterOptions { HasText = "api's setup works now" });
        await Assertions.Expect(works.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Open a pull request" })).ToBeVisibleAsync();
        await WebBrowser.ShotAsync(page, "setup-works");

        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Please write hello.txt in a new chat" }).ClickAsync();
        ILocator fixedElsewhere = page.GetByRole(AriaRole.Alert).Filter(new LocatorFilterOptions { HasText = "api's setup is fixed in another chat" });
        await Assertions.Expect(fixedElsewhere).ToBeVisibleAsync(WebBrowser.Shown);
        await WebBrowser.ShotAsync(page, "setup-fixed-in-another-chat");
        await fixedElsewhere.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Use the fix here" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Set up again in ", new PageGetByTextOptions { Exact = false })).ToBeVisibleAsync(WebBrowser.Shown);
        await Assertions.Expect(page.GetByRole(AriaRole.Alert).Filter(new LocatorFilterOptions { HasText = "setup" })).ToHaveCountAsync(0);
        await WebBrowser.ShotAsync(page, "setup-fix-used");
    }

    // The changes panel reads the changes again while it's open and the agent works. Someone reading
    // a long file, picked in the tree, stays at the same line as a file above it grows (here the journey
    // changes the files, as the agent would, while the model holds its turn).
    private async Task TheChangesStayLiveAndKeepTheReadersPlaceAsync(IPage page, TestWorkspace acme, ChatSummary chat)
    {
        await acme.ChangeAsync(chat.NookId, "mkdir -p /work/api/notes && echo 'first note' > /work/api/notes/a.txt && seq 1 150 | sed 's/^/line /' > /work/api/notes/long.txt");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Changes" }).ClickAsync();
        ILocator panel = page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Changes" });
        await Assertions.Expect(panel.GetByText("3 files in api")).ToBeVisibleAsync(WebBrowser.Shown);
        await panel.GetByRole(AriaRole.Treeitem, new LocatorGetByRoleOptions { Name = "long.txt" }).ClickAsync();
        ILocator read = panel.GetByText("line 20", new LocatorGetByTextOptions { Exact = true });
        LocatorBoundingBoxResult? before = await read.BoundingBoxAsync();

        app.Model.HoldOn(Wait);
        await acme.SendAsync(chat, Wait);
        await app.Model.HeldAsync(Wait);
        await acme.ChangeAsync(chat.NookId, "seq 1 40 | sed 's/^/more of the first note /' >> /work/api/notes/a.txt");
        await Assertions.Expect(panel.GetByText("+192", new LocatorGetByTextOptions { Exact = true }).First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        LocatorBoundingBoxResult? after = await read.BoundingBoxAsync();
        await WebBrowser.ShotAsync(page, "changes-live");
        app.Model.Release(Wait);
        await page.Keyboard.PressAsync("Escape");

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(before.Y, after.Y, 2);
    }

    // The agent changes the repository (here the journey does, as it would); the changes, the setup's
    // fix among them, show as a diff, the files download, and a pull request opens on GitHub with them.
    private async Task ReviewsTheChangesDownloadsTheFilesAndOpensAPullRequestAsync(IPage page, TestWorkspace acme, ChatSummary chat, string owner)
    {
        await acme.ChangeAsync(chat.NookId, "echo 'hello from the review' > /work/api/CHANGE.md");

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Changes" }).ClickAsync();
        ILocator panel = page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Changes" });
        await Assertions.Expect(panel.GetByText("4 files in api")).ToBeVisibleAsync(WebBrowser.Shown);
        await panel.GetByRole(AriaRole.Treeitem, new LocatorGetByRoleOptions { Name = "CHANGE.md" }).ClickAsync();
        await Assertions.Expect(panel.GetByText("hello from the review")).ToBeVisibleAsync(WebBrowser.Shown);
        await WebBrowser.ShotAsync(page, "changes");
        IDownload download = await page.RunAndWaitForDownloadAsync(() => panel.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Download files" }).ClickAsync());
        string archive = await download.PathAsync();
        await page.Keyboard.PressAsync("Escape");

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Push" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = "Open a pull request" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Pushed to aisloth/")).ToBeVisibleAsync(WebBrowser.Shown);
        await WebBrowser.ShotAsync(page, "pushed");
        string branch = "aisloth/" + chat.Id.Value.ToString("N", CultureInfo.InvariantCulture)[^6..];
        string? file = await app.GitHub.ReadAsync(owner, "api", "show", branch + ":CHANGE.md");
        string? setup = await app.GitHub.ReadAsync(owner, "api", "show", branch + ":.agents/setup");

        Assert.EndsWith(".tar.gz", download.SuggestedFilename, StringComparison.Ordinal);
        Assert.True(new FileInfo(archive).Length > 0);
        Assert.Equal("hello from the review", file);
        Assert.Equal("#!/bin/sh\necho installed", setup);
        Assert.Single(app.GitHub.PullRequests(owner, "api"));
    }

    private static async Task AMessageStartsTheNextTurnAsync(IPage page)
    {
        await Composer(page).FillAsync("say hello");
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.GetByText("say hello", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText(TurnEnded())).ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 60_000 });
    }

    // Selecting a whole message, as three clicks do, copies its text alone, without the line break
    // after it that the selection takes in.
    private static async Task AMessageCopiesAsItsTextAsync(IPage page, IBrowserContext context)
    {
        await context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await page.GetByText("say hello", new PageGetByTextOptions { Exact = true }).ClickAsync(new LocatorClickOptions { ClickCount = 3 });
        await page.Keyboard.PressAsync("ControlOrMeta+C");

        string copied = await page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        Assert.Equal("say hello", copied);
    }

    // The transcript follows the agent's newest output while the reader is at its end. Scrolled away,
    // it stays where they put it as output arrives, and its button scrolls to the end, which follows
    // again. A short window makes the transcript scroll.
    private static async Task TheTranscriptFollowsOnlyWhileAtItsEndAsync(IPage page, TestWorkspace acme, ChatSummary chat)
    {
        await page.SetViewportSizeAsync(1440, 420);
        ILocator viewport = page.Locator("[data-slot=message-scroller-viewport]");
        ILocator toEnd = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Scroll to end" });
        ILocator replies = page.GetByText("Hello.", new PageGetByTextOptions { Exact = true });
        int replied = await replies.CountAsync();
        await viewport.HoverAsync();
        await page.Mouse.WheelAsync(0, -300);
        await Assertions.Expect(toEnd).ToHaveAttributeAsync("data-active", "true");
        double before = await viewport.EvaluateAsync<double>("node => node.scrollTop");

        await acme.SendAsync(chat, "say hello from afar");
        await Assertions.Expect(replies).ToHaveCountAsync(replied + 1, new LocatorAssertionsToHaveCountOptions { Timeout = 60_000 });
        double after = await viewport.EvaluateAsync<double>("node => node.scrollTop");
        await WebBrowser.ShotAsync(page, "scrolled-away");
        await toEnd.ClickAsync();
        await Assertions.Expect(toEnd).ToHaveAttributeAsync("data-active", "false");
        await acme.SendAsync(chat, "say hello once more");
        await Assertions.Expect(replies).ToHaveCountAsync(replied + 2, new LocatorAssertionsToHaveCountOptions { Timeout = 60_000 });
        bool atEnd = await viewport.EvaluateAsync<bool>("node => node.scrollHeight - node.scrollTop - node.clientHeight < 2");
        await WebBrowser.ShotAsync(page, "following");
        await page.SetViewportSizeAsync(1440, 900);

        Assert.Equal(before, after, 1);
        Assert.True(atEnd);
    }

    // The model holds a call that says Wait, so the turn runs until it's released.
    private async Task AMessageWhileTheAgentWorksGoesIntoItsTurnAsync(IPage page)
    {
        app.Model.HoldOn(Wait);
        await Composer(page).FillAsync(Wait);
        await page.Keyboard.PressAsync("Enter");
        await app.Model.HeldAsync(Wait);
        await Assertions.Expect(page.GetByRole(AriaRole.Status).Filter(new LocatorFilterOptions { HasText = "Working" })).ToBeVisibleAsync(WebBrowser.Shown);

        await Composer(page).FillAsync("and then say hello");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.GetByText("Sent into the running turn")).ToBeVisibleAsync(WebBrowser.Shown);
        await WebBrowser.ShotAsync(page, "steered");
        app.Model.Release(Wait);

        await Assertions.Expect(page.GetByText(TurnEnded())).ToHaveCountAsync(3, new LocatorAssertionsToHaveCountOptions { Timeout = 60_000 });
    }

    private static async Task EscStopsTheTurnAsync(IPage page)
    {
        await Composer(page).FillAsync(Wait);
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.GetByRole(AriaRole.Status).Filter(new LocatorFilterOptions { HasText = "Working" })).ToBeVisibleAsync(WebBrowser.Shown);

        await Composer(page).PressAsync("Escape");

        await Assertions.Expect(page.GetByText("The turn was stopped")).ToBeVisibleAsync(WebBrowser.Shown);
        await Assertions.Expect(page.GetByRole(AriaRole.Status).Filter(new LocatorFilterOptions { HasText = "Working" })).ToBeHiddenAsync();
    }

    private static async Task TheSessionIsACookieNoScriptReadsAndNothingBrokeThePolicyAsync(IPage page, IBrowserContext context)
    {
        IReadOnlyList<BrowserContextCookiesResult> cookies = await context.CookiesAsync();
        BrowserContextCookiesResult session = Assert.Single(cookies, cookie => string.Equals(cookie.Name, "__Host-aisloth-session", StringComparison.Ordinal));
        string readable = await page.EvaluateAsync<string>("document.cookie");
        string[] violations = await WebBrowser.ViolationsAsync(page);

        Assert.True(session.HttpOnly);
        Assert.True(session.Secure);
        Assert.Equal(SameSiteAttribute.Strict, session.SameSite);
        Assert.DoesNotContain("aisloth-session", readable, StringComparison.Ordinal);
        Assert.Empty(violations);
    }

    private async Task ALinkToTheChatSignsInThroughTheProviderAsync(WebBrowser browser, ChatSummary chat, string person)
    {
        await using IBrowserContext context = await browser.NewContextAsync();
        IPage page = await context.NewPageAsync();
        await WebBrowser.SignInThroughTheProviderAsAsync(page, person);

        await page.GotoAsync(new Uri(app.WebApiUrl, string.Create(CultureInfo.InvariantCulture, $"app/chats/{chat.Id.Value}")).AbsoluteUri);
        await Assertions.Expect(page).ToHaveURLAsync(SignInPage());
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue with Fake" }).ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Please write hello.txt for me" })).ToBeVisibleAsync(WebBrowser.Shown);
        await Assertions.Expect(page.GetByText("The turn was stopped")).ToBeVisibleAsync(WebBrowser.Shown);
        await WebBrowser.ShotAsync(page, "provider");
    }

    private static ILocator Composer(IPage page)
    {
        return page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = "Message", Exact = true });
    }

    [GeneratedRegex("/app/sign-in", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SignInPage();

    [GeneratedRegex("^Turn ended", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TurnEnded();
}
