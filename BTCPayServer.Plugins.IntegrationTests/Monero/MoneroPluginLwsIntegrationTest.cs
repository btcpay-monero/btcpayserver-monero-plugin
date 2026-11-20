using BTCPayServer.Plugins.Monero.Data;
using BTCPayServer.Plugins.Monero.Services;

using Microsoft.Extensions.Hosting;

using Xunit;

namespace BTCPayServer.Plugins.IntegrationTests.Monero;

[Collection("Mining")]
public class MoneroPluginLwsIntegrationTest(ITestOutputHelper helper) : MoneroLwsIntegrationTestBase(helper)
{

    [Fact]
    public async Task ShouldSetupStoreWithXmr()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await s.RegisterNewUser(true);
        await s.CreateNewStore();

        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();

        var lwsIsAvailable = await s.Page
            .Locator("li.list-group-item:text('Light wallet server available: True')")
            .InnerTextAsync();

        Assert.Contains("Light wallet server available: True", lwsIsAvailable);
    }

    [Fact]
    public async Task ShouldLoadViewWalletOnStartUpIfExistsInDb()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await IntegrationTestUtils.CreateDbRecordWithXmrWallet(MoneroStoreWallet.DefaultKey,
            "9yEzCbcYdg6MqZ5AkEh8V3YCriyN1tvmtWEHdBEUHkF6D6kN1MMD2Kd2QVWoTY67aNHNYKMUP3xfteLS2QNavJxpJdx6mWj",
            "1f4668e8c1979b4c7dae13dc149fd95cd7ff2883becffe160c21f9e02c821c08");

        await s.RegisterNewUser(true);
        await s.CreateNewStore();

        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();

        var walletNotAvailable = await s.Page
            .Locator("li.list-group-item:text('Wallet available: False')")
            .InnerTextAsync();

        Assert.Contains("Wallet available: False", walletNotAvailable);

        // Re-run the startup logic, now that the record exists
        var loadUp = s.Server.PayTester
            .GetService<IEnumerable<IHostedService>>()
            .OfType<MoneroLwsLoadUpService>()
            .Single();
        await loadUp.StartAsync(CancellationToken.None);

        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();

        var walletAvailable = await s.Page
            .Locator("li.list-group-item:text('Wallet available: True')")
            .InnerTextAsync();

        Assert.Contains("Wallet available: True", walletAvailable);
    }
}