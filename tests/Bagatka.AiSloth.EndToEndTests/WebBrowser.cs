using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Chromium for the journeys through the web app the host serves: Playwright's, or the one
/// <c>BAGATKA_CHROMIUM</c> names. Each of its pages records the content security policy violations it
/// sees, and journeys take screenshots of them for review in <c>TestResults/web</c>.
/// </summary>
internal sealed partial class WebBrowser : IAsyncDisposable
{
    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;

    private WebBrowser(IPlaywright playwright, IBrowser browser)
    {
        _playwright = playwright;
        _browser = browser;
    }

    /// <summary>How long a step may take to show: an agent's turn, from the page's point of view.</summary>
    public static LocatorAssertionsToBeVisibleOptions Shown { get; } = new LocatorAssertionsToBeVisibleOptions { Timeout = 60_000 };

    public static async Task<WebBrowser> LaunchAsync()
    {
        IPlaywright playwright = await Playwright.CreateAsync();
        IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            ExecutablePath = Environment.GetEnvironmentVariable("BAGATKA_CHROMIUM") is { Length: > 0 } chromium ? chromium : null,
        });
        return new WebBrowser(playwright, browser);
    }

    /// <summary>A browser of its own, as on another computer, without cookies.</summary>
    public async Task<IBrowserContext> NewContextAsync()
    {
        IBrowserContext context = await _browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1440, Height = 900 } });
        await context.AddInitScriptAsync("window.violations = []; document.addEventListener('securitypolicyviolation', (event) => window.violations.push(event.violatedDirective + ' ' + event.blockedURI))");
        return context;
    }

    /// <summary>What the page's content security policy blocked, as "directive URI".</summary>
    public static async Task<string[]> ViolationsAsync(IPage page)
    {
        return await page.EvaluateAsync<string[]>("window.violations");
    }

    /// <summary>
    /// Has the host's fake provider sign in <paramref name="person"/> when the page signs in through it,
    /// as it signs in whoever <c>login_hint</c> names, which the app never sends.
    /// </summary>
    public static async Task SignInThroughTheProviderAsAsync(IPage page, string person)
    {
        await page.RouteAsync(ProviderSignInStart(), route => route.ContinueAsync(new RouteContinueOptions { Url = route.Request.Url + "&login_hint=" + Uri.EscapeDataString(person) }));
    }

    /// <summary>
    /// A screenshot for review, after waiting up to two seconds for what moves to settle, such as a
    /// panel sliding in; some animations never end.
    /// </summary>
    public static async Task ShotAsync(IPage page, string name)
    {
        string shots = Path.Combine(AppContext.BaseDirectory, "TestResults", "web");
        Directory.CreateDirectory(shots);
        await page.EvaluateAsync("() => Promise.race([Promise.all(document.getAnimations().map(animation => animation.finished.catch(() => {}))), new Promise(settled => setTimeout(settled, 2000))])");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(shots, name + ".png") });
    }

    public async ValueTask DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    [GeneratedRegex("/sign-in/browser\\?", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProviderSignInStart();
}
