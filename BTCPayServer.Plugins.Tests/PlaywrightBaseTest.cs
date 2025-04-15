using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Tests;
using BTCPayServer.Views.Stores;
using Microsoft.Playwright;
using NBitcoin;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests;

public class PlaywrightBaseTest : UnitTestBase, IDisposable
{
    private string CreatedUser;
    private string InvoiceId;

    public PlaywrightBaseTest(ITestOutputHelper helper) : base(helper)
    {
    }

    public WalletId WalletId { get; set; }
    public IPlaywright Playwright { get; private set; }
    public IBrowser Browser { get; private set; }
    public IPage Page { get; private set; }
    public Uri ServerUri { get; private set; }
    public string Password { get; private set; }
    public string StoreId { get; private set; }
    public bool IsAdmin { get; private set; }

    public void Dispose()
    {
        static void Try(Action action)
        {
            try
            {
                action();
            }
            catch { }
        }

        Try(() =>
        {
            Page?.CloseAsync().GetAwaiter().GetResult();
            Page = null;
        });

        Try(() =>
        {
            Browser?.CloseAsync().GetAwaiter().GetResult();
            Browser = null;
        });

        Try(() =>
        {
            Playwright?.Dispose();
            Playwright = null;
        });
    }


    public async Task InitializePlaywright(Uri uri)
    {
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true, // Set to true for CI/automated environments... and false, for real-time local testing 
            SlowMo = 50 // Add slight delay between actions to improve stability
        });
        var context = await Browser.NewContextAsync();
        Page = await context.NewPageAsync();
        ServerUri = uri;
        TestLogs.LogInformation($"Playwright: Browsing to {ServerUri}");
    }

    public async Task GoToUrl(string relativeUrl)
    {
        await Page.GotoAsync(Link(relativeUrl));
    }

    public string Link(string relativeLink)
    {
        return ServerUri.AbsoluteUri.WithoutEndingSlash() + relativeLink.WithStartingSlash();
    }

    public async Task<string> RegisterNewUser(bool isAdmin = false)
    {
        var usr = RandomUtils.GetUInt256().ToString().Substring(64 - 20) + "@a.com";
        await Page.FillAsync("#Email", usr);
        await Page.FillAsync("#Password", "123456");
        await Page.FillAsync("#ConfirmPassword", "123456");
        if (isAdmin)
            await Page.ClickAsync("#IsAdmin");

        await Page.ClickAsync("#RegisterButton");
        CreatedUser = usr;
        Password = "123456";
        IsAdmin = isAdmin;
        return usr;
    }

    public async Task GoToStore(StoreNavPages storeNavPage = StoreNavPages.General)
    {
        await GoToStore(null, storeNavPage);
    }

    public async Task GoToStore(string storeId, StoreNavPages storeNavPage = StoreNavPages.General)
    {
        if (storeId is not null)
        {
            await GoToUrl($"/stores/{storeId}/");
            StoreId = storeId;
            if (WalletId != null)
                WalletId = new WalletId(storeId, WalletId.CryptoCode);
        }

        await Page.Locator($"#StoreNav-{storeNavPage}").ClickAsync();
    }

    public async Task<(string storeName, string storeId)> CreateNewStoreAsync(bool keepId = true)
    {
        if (await Page.Locator("#StoreSelectorToggle").IsVisibleAsync()) await Page.Locator("#StoreSelectorToggle").ClickAsync();
        await GoToUrl("/stores/create");
        var name = "Store" + RandomUtils.GetUInt64();
        TestLogs.LogInformation($"Created store {name}");
        await Page.FillAsync("#Name", name);

        var selectedOption = await Page.Locator("#PreferredExchange option:checked").TextContentAsync();
        Assert.Equal("Recommendation (Kraken)", selectedOption.Trim());
        await Page.Locator("#PreferredExchange").SelectOptionAsync(new SelectOptionValue { Label = "CoinGecko" });
        await Page.ClickAsync("#Create");
        await Page.ClickAsync("#StoreNav-General");
        var storeId = await Page.InputValueAsync("#Id");
        if (keepId)
            StoreId = storeId;

        return (name, storeId);
    }

    public async Task InitializeBTCPayServer()
    {
        await GoToUrl("/register");
        await RegisterNewUser(true);
        await CreateNewStoreAsync();
        await GoToStore();
        // await AddMoneroPlugin();
    }
}
