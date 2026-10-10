using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Contracts;
using Microsoft.Playwright;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: someone new sets up their workspace from the browser alone. Signed in through the host's
/// provider for the first time, they find their chats need an agent account and add one, connect
/// GitHub and add a repository, set a secret, write instructions for every agent, and invite someone,
/// who joins from their own browser. Then a chat can start with the repository. Only the model and
/// GitHub are fake.
/// </summary>
public sealed class WebSetupJourney(ControlPlane app)
{
    [Fact]
    public async Task Someone_new_sets_up_their_workspace_in_the_web_app()
    {
        string suffix = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)[^8..];
        string erin = "erin-" + suffix;
        string frank = "frank-" + suffix;
        string owner = "erin-co-" + suffix;
        await app.GitHub.CreateRepositoryAsync(owner, "api", erin);
        using HttpClient frankApi = app.ClientFor(frank);
        await Api.ReadAsync<UserProfile>(frankApi.SendGetAsync("/users/me"), HttpStatusCode.OK);

        await using WebBrowser browser = await WebBrowser.LaunchAsync();
        await using IBrowserContext context = await browser.NewContextAsync();
        IPage page = await context.NewPageAsync();
        try
        {
            await SignsInForTheFirstTimeAndIsAskedForAnAgentAccountAsync(page, erin);
            await AddsAnAgentAccountForTheirWorkspaceAsync(page);
            await ConnectsGitHubAndAddsARepositoryAsync(page, erin, owner);
            await SetsASecretAsync(page);
            await WritesInstructionsForEveryAgentAsync(page);
            string code = await InvitesSomeoneAsync(page);
            await TheyJoinFromTheirOwnBrowserAsync(browser, frank, code);
            await AChatCanStartWithTheRepositoryAsync(page);
            string[] violations = await WebBrowser.ViolationsAsync(page);
            Assert.Empty(violations);
        }
        finally
        {
            await WebBrowser.ShotAsync(page, "setup-end");
        }
    }

    private async Task SignsInForTheFirstTimeAndIsAskedForAnAgentAccountAsync(IPage page, string person)
    {
        await WebBrowser.SignInThroughTheProviderAsAsync(page, person);
        await page.GotoAsync(new Uri(app.WebApiUrl, "app/").AbsoluteUri);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue with Fake" }).ClickAsync();

        await Assertions.Expect(page.GetByText("No agent account yet")).ToBeVisibleAsync(WebBrowser.Shown);
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Add an agent account" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Settings" })).ToBeVisibleAsync();
    }

    // An Anthropic API key at the fake model, for everyone in the workspace.
    private async Task AddsAnAgentAccountForTheirWorkspaceAsync(IPage page)
    {
        ILocator accounts = Section(page, "Agent accounts");
        await accounts.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add" }).ClickAsync();
        ILocator dialog = page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Add an agent account" });
        await dialog.GetByLabel("Name").FillAsync("Team key");
        await dialog.GetByLabel("API key").FillAsync(FakeModel.ApiKey);
        await dialog.GetByLabel("Endpoint, if not the vendor's own").FillAsync(app.Model.Url.AbsoluteUri);
        await WebBrowser.ShotAsync(page, "setup-account");
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add account" }).ClickAsync();

        await Assertions.Expect(accounts.GetByText("Team key")).ToBeVisibleAsync(WebBrowser.Shown);
        await Assertions.Expect(accounts.GetByText("Anthropic API key")).ToBeVisibleAsync();
    }

    // GitHub's device flow: the page shows the code, which the person approves at GitHub.
    private async Task ConnectsGitHubAndAddsARepositoryAsync(IPage page, string login, string owner)
    {
        ILocator repositories = Section(page, "Repositories");
        await repositories.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Connect GitHub" }).ClickAsync();
        ILocator connecting = page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Connect GitHub" });
        ILocator code = connecting.Locator("code");
        await Assertions.Expect(code).ToBeVisibleAsync(WebBrowser.Shown);
        string shown = await code.InnerTextAsync();
        app.GitHub.Approve(shown, login);
        await Assertions.Expect(repositories.GetByText("Your GitHub account, connected")).ToBeVisibleAsync(WebBrowser.Shown);

        await repositories.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add" }).ClickAsync();
        ILocator adding = page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Add repositories" });
        ILocator available = adding.GetByRole(AriaRole.Listitem).Filter(new LocatorFilterOptions { HasText = owner + "/api" });
        await available.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add" }).ClickAsync();
        await Assertions.Expect(adding.GetByText("Every repository it reaches is added.")).ToBeVisibleAsync(WebBrowser.Shown);
        await page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(repositories.GetByText(owner + "/api")).ToBeVisibleAsync();
    }

    private static async Task SetsASecretAsync(IPage page)
    {
        ILocator secrets = Section(page, "Secrets");
        await secrets.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Set" }).ClickAsync();
        ILocator dialog = page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Set a secret" });
        await dialog.GetByLabel("Name").FillAsync("GREETING");
        await dialog.GetByLabel("Value").FillAsync("hello from the settings");
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Set secret" }).ClickAsync();

        await Assertions.Expect(secrets.GetByText("GREETING")).ToBeVisibleAsync(WebBrowser.Shown);
    }

    private static async Task WritesInstructionsForEveryAgentAsync(IPage page)
    {
        ILocator instructions = Section(page, "Instructions");
        await instructions.GetByLabel("For everyone's chats").FillAsync("Say hi before you start.");
        await instructions.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();

        await Assertions.Expect(page.GetByText("Instructions saved")).ToBeVisibleAsync(WebBrowser.Shown);
        await Assertions.Expect(instructions.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" })).ToBeHiddenAsync();
        await WebBrowser.ShotAsync(page, "setup-settings");
    }

    private static async Task<string> InvitesSomeoneAsync(IPage page)
    {
        ILocator people = Section(page, "People");
        await people.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Invite" }).ClickAsync();
        ILocator dialog = page.GetByRole(AriaRole.Dialog);
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create invite" }).ClickAsync();
        ILocator code = dialog.Locator("code");
        await Assertions.Expect(code).ToBeVisibleAsync(WebBrowser.Shown);
        string invite = await code.InnerTextAsync();
        await WebBrowser.ShotAsync(page, "setup-invite");
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Done" }).ClickAsync();
        return invite;
    }

    // Someone who already uses AiSloth here joins from the workspace menu, and lands in the workspace.
    private async Task TheyJoinFromTheirOwnBrowserAsync(WebBrowser browser, string person, string code)
    {
        await using IBrowserContext theirs = await browser.NewContextAsync();
        IPage page = await theirs.NewPageAsync();
        await WebBrowser.SignInThroughTheProviderAsAsync(page, person);
        await page.GotoAsync(new Uri(app.WebApiUrl, "app/").AbsoluteUri);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continue with Fake" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = person }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = "Join a workspace" }).ClickAsync();
        await page.GetByLabel("Invite").FillAsync(code);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Join", Exact = true }).ClickAsync();

        await Assertions.Expect(page.GetByText("You joined the workspace")).ToBeVisibleAsync(WebBrowser.Shown);
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "What should Claude Code work on?" })).ToBeVisibleAsync(WebBrowser.Shown);
    }

    // The new chat page offers the account's agent and the repository.
    private static async Task AChatCanStartWithTheRepositoryAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "New chat" }).ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "What should Claude Code work on?" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "No repository" })).ToBeVisibleAsync();
    }

    private static ILocator Section(IPage page, string title)
    {
        return page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = title, Exact = true });
    }
}
