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
/// agent, review what changed in the repository, download its files, and open a pull request. The app
/// runs under the host's content security policy without a violation, and its session is a cookie no
/// script can read. On another computer, a link to the chat signs them in through the host's provider
/// and opens it. Only the model and GitHub are fake; the app is src/Apps/Web as last built, and
/// the browser is Chromium, Playwright's or the one <c>BAGATKA_CHROMIUM</c> names.
/// </summary>
public sealed partial class WebJourney(ControlPlane app)
{
    // What the model holds on in this journey; ChatJourney, on the same host, holds on its own.
    private const string Wait = "wait a moment";

    // How long a step may take to show: an agent's turn, from the page's point of view.
    private static readonly LocatorAssertionsToBeVisibleOptions Shown = new LocatorAssertionsToBeVisibleOptions { Timeout = 60_000 };

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

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            ExecutablePath = Environment.GetEnvironmentVariable("BAGATKA_CHROMIUM") is { Length: > 0 } chromium ? chromium : null,
        });
        await using IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1440, Height = 900 } });
        await context.AddInitScriptAsync("window.violations = []; document.addEventListener('securitypolicyviolation', (event) => window.violations.push(event.violatedDirective + ' ' + event.blockedURI))");
        IPage page = await context.NewPageAsync();

        try
        {
            await SignsInWithALinkCodeAsync(page, code.Code);
            await AWorkspaceWithoutAnAccountSaysWhatChatsNeedAsync(page);
            await FindsTheChatAndSeesItsTurnAsync(page);
            await AChatStartedElsewhereShowsUpAtOnceAsync(page, acme);
            await AMessageStartsTheNextTurnAsync(page);
            await AMessageWhileTheAgentWorksGoesIntoItsTurnAsync(page);
            await EscStopsTheTurnAsync(page);
            ChatSummary started = await StartsAChatWithTheRepositoryInTheAppAsync(page, acme);
            await ReviewsTheChangesDownloadsTheFilesAndOpensAPullRequestAsync(page, acme, started, owner);
            await TheSessionIsACookieNoScriptReadsAndNothingBrokeThePolicyAsync(page, context);
            await ALinkToTheChatSignsInThroughTheProviderAsync(browser, chat, alice);
        }
        finally
        {
            // What the page looked like at the end, or where it failed, in both themes.
            await ShotAsync(page, "end");
            await page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = ColorScheme.Dark });
            await ShotAsync(page, "end-dark");
            app.Model.ReleaseForGood(Wait);
        }
    }

    private async Task SignsInWithALinkCodeAsync(IPage page, string code)
    {
        IResponse? response = await page.GotoAsync(new Uri(app.WebApiUrl, "app/").AbsoluteUri);
        Assert.True(response is { Ok: true }, "The host serves no web app: build it first, with npm run build in src/Apps/Web.");
        await Assertions.Expect(page).ToHaveURLAsync(SignInPage());

        await page.GetByLabel("Code").FillAsync(code);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in", Exact = true }).ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Searchbox).Or(page.GetByLabel("Search chats"))).ToBeVisibleAsync(Shown);
    }

    // The app opens with the person's own workspace, where they added no agent account.
    private static async Task AWorkspaceWithoutAnAccountSaysWhatChatsNeedAsync(IPage page)
    {
        await Assertions.Expect(page.GetByText("No agent account yet")).ToBeVisibleAsync(Shown);
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Send" })).ToBeDisabledAsync();
        await ShotAsync(page, "no-account");
    }

    // The chat is in Acme.
    private static async Task FindsTheChatAndSeesItsTurnAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "alice" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = "Acme" }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Please write hello.txt for me" }).ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Please write hello.txt for me" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Write").First).ToBeVisibleAsync(Shown);
        await Assertions.Expect(page.GetByText("Done.")).ToBeVisibleAsync(Shown);
        await Assertions.Expect(page.GetByText(TurnEnded()).First).ToBeVisibleAsync(Shown);
        await ShotAsync(page, "first-turn");
    }

    // Someone starts a chat from sloth or another browser: the list shows it without a reload.
    private static async Task AChatStartedElsewhereShowsUpAtOnceAsync(IPage page, TestWorkspace acme)
    {
        ChatSummary elsewhere = await acme.StartChatAsync();
        await acme.SendAsync(elsewhere, "hello from the terminal");

        await Assertions.Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "hello from the terminal" })).ToBeVisibleAsync(Shown);
    }

    // With sloth or, later, the app: their GitHub account, and Acme's repository from it. Returns the
    // repository's owner on GitHub.
    private async Task<string> ConnectsGitHubAndAddsARepositoryAsync(HttpClient person, TestWorkspace acme)
    {
        string suffix = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)[^8..];
        string login = "alice-" + suffix;
        string owner = "acme-" + suffix;
        await app.GitHub.CreateRepositoryAsync(owner, "api", login);
        GitHubConnectionStarted connecting = await Api.ReadAsync<GitHubConnectionStarted>(person.SendPostAsync("/github/connections", new { }), HttpStatusCode.OK);
        app.GitHub.Approve(connecting.UserCode, login);
        await Api.ExpectAsync(person.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/github/connections/{connecting.Id.Value}/complete"), new { }), HttpStatusCode.OK);
        await Api.ExpectAsync(person.SendPostAsync(acme.Path + "/repositories", new { fullName = owner + "/api" }), HttpStatusCode.Created);
        return owner;
    }

    // The agent picker offers each account's harnesses, and the repository picker the workspace's
    // repositories; the nook starts as the message is written.
    private static async Task<ChatSummary> StartsAChatWithTheRepositoryInTheAppAsync(IPage page, TestWorkspace acme)
    {
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "New chat" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "What should Claude Code work on?" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Claude Code" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitemradio, new PageGetByRoleOptions { Name = "Claude Code" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "No repository" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitemcheckbox, new PageGetByRoleOptions { Name = "api" }).ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "api" })).ToBeVisibleAsync();
        await ShotAsync(page, "new-chat");

        await Composer(page).FillAsync("Please write hello.txt in a new chat");
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Please write hello.txt in a new chat" })).ToBeVisibleAsync(Shown);
        await Assertions.Expect(page.GetByText("Done.")).ToBeVisibleAsync(Shown);
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Please write hello.txt in a new chat" })).ToBeVisibleAsync();
        await ShotAsync(page, "started");
        Page<ChatSummary> chats = await Api.ReadAsync<Page<ChatSummary>>(acme.Person.SendGetAsync(acme.Path + "/chats"), HttpStatusCode.OK);
        return chats.Items[0];
    }

    // The agent changes the repository (here the journey does, as it would); the changes show as a
    // diff, the files download, and a pull request opens on GitHub with them.
    private async Task ReviewsTheChangesDownloadsTheFilesAndOpensAPullRequestAsync(IPage page, TestWorkspace acme, ChatSummary chat, string owner)
    {
        await acme.ChangeAsync(chat.NookId, "echo 'hello from the review' > /work/api/CHANGE.md");

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Changes" }).ClickAsync();
        ILocator panel = page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Changes" });
        await Assertions.Expect(panel.GetByText("1 file in api")).ToBeVisibleAsync(Shown);
        await Assertions.Expect(panel.GetByText("hello from the review")).ToBeVisibleAsync(Shown);
        await ShotAsync(page, "changes");
        IDownload download = await page.RunAndWaitForDownloadAsync(() => panel.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Download files" }).ClickAsync());
        string archive = await download.PathAsync();
        await page.Keyboard.PressAsync("Escape");

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Push" }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = "Open a pull request" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Pushed to aisloth/")).ToBeVisibleAsync(Shown);
        await ShotAsync(page, "pushed");
        string? file = await app.GitHub.ReadAsync(owner, "api", "show", "aisloth/" + chat.Id.Value.ToString("N", CultureInfo.InvariantCulture)[^6..] + ":CHANGE.md");

        Assert.EndsWith(".tar.gz", download.SuggestedFilename, StringComparison.Ordinal);
        Assert.True(new FileInfo(archive).Length > 0);
        Assert.Equal("hello from the review", file);
        Assert.Single(app.GitHub.PullRequests(owner, "api"));
    }

    private static async Task AMessageStartsTheNextTurnAsync(IPage page)
    {
        await Composer(page).FillAsync("say hello");
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.GetByText("say hello", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText(TurnEnded())).ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 60_000 });
    }

    // The model holds a call that says Wait, so the turn runs until it's released.
    private async Task AMessageWhileTheAgentWorksGoesIntoItsTurnAsync(IPage page)
    {
        app.Model.HoldOn(Wait);
        await Composer(page).FillAsync(Wait);
        await page.Keyboard.PressAsync("Enter");
        await app.Model.HeldAsync(Wait);
        await Assertions.Expect(page.GetByRole(AriaRole.Status).GetByText("is working")).ToBeVisibleAsync(Shown);

        await Composer(page).FillAsync("and then say hello");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.GetByText("Sent into the running turn")).ToBeVisibleAsync(Shown);
        await ShotAsync(page, "steered");
        app.Model.Release(Wait);

        await Assertions.Expect(page.GetByText(TurnEnded())).ToHaveCountAsync(3, new LocatorAssertionsToHaveCountOptions { Timeout = 60_000 });
    }

    private static async Task EscStopsTheTurnAsync(IPage page)
    {
        await Composer(page).FillAsync(Wait);
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.GetByRole(AriaRole.Status).GetByText("is working")).ToBeVisibleAsync(Shown);

        await Composer(page).PressAsync("Escape");

        await Assertions.Expect(page.GetByText("The turn was stopped")).ToBeVisibleAsync(Shown);
        await Assertions.Expect(page.GetByRole(AriaRole.Status).GetByText("is working")).ToBeHiddenAsync();
    }

    private static async Task TheSessionIsACookieNoScriptReadsAndNothingBrokeThePolicyAsync(IPage page, IBrowserContext context)
    {
        IReadOnlyList<BrowserContextCookiesResult> cookies = await context.CookiesAsync();
        BrowserContextCookiesResult session = Assert.Single(cookies, cookie => string.Equals(cookie.Name, "__Host-aisloth-session", StringComparison.Ordinal));
        string readable = await page.EvaluateAsync<string>("document.cookie");
        string[] violations = await page.EvaluateAsync<string[]>("window.violations");

        Assert.True(session.HttpOnly);
        Assert.True(session.Secure);
        Assert.Equal(SameSiteAttribute.Strict, session.SameSite);
        Assert.DoesNotContain("aisloth-session", readable, StringComparison.Ordinal);
        Assert.Empty(violations);
    }

    // The fake provider signs in whoever login_hint names, which the app never sends: the step adds it
    // to the app's way into sign-in.
    private async Task ALinkToTheChatSignsInThroughTheProviderAsync(IBrowser browser, ChatSummary chat, string person)
    {
        await using IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1440, Height = 900 } });
        IPage page = await context.NewPageAsync();
        await page.RouteAsync(ProviderSignInStart(), route => route.ContinueAsync(new RouteContinueOptions { Url = route.Request.Url + "&login_hint=" + Uri.EscapeDataString(person) }));

        await page.GotoAsync(new Uri(app.WebApiUrl, string.Create(CultureInfo.InvariantCulture, $"app/chats/{chat.Id.Value}")).AbsoluteUri);
        await Assertions.Expect(page).ToHaveURLAsync(SignInPage());
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue with Fake" }).ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Please write hello.txt for me" })).ToBeVisibleAsync(Shown);
        await Assertions.Expect(page.GetByText("The turn was stopped")).ToBeVisibleAsync(Shown);
        await ShotAsync(page, "provider");
    }

    // Screenshots for review, beside the test results: TestResults/web. They wait up to two seconds
    // for what's moving to settle, such as a panel sliding in; some animations never end.
    private static async Task ShotAsync(IPage page, string name)
    {
        string shots = Path.Combine(AppContext.BaseDirectory, "TestResults", "web");
        Directory.CreateDirectory(shots);
        await page.EvaluateAsync("() => Promise.race([Promise.all(document.getAnimations().map(animation => animation.finished.catch(() => {}))), new Promise(settled => setTimeout(settled, 2000))])");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(shots, name + ".png") });
    }

    private static ILocator Composer(IPage page)
    {
        return page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = "Message", Exact = true });
    }

    [GeneratedRegex("/app/sign-in", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SignInPage();

    [GeneratedRegex("^Turn ended", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TurnEnded();

    [GeneratedRegex("/sign-in/browser\\?", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProviderSignInStart();
}
