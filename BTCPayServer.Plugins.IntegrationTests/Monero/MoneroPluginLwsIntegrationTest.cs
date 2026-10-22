using Xunit;

namespace BTCPayServer.Plugins.IntegrationTests.Monero;

[Collection("Mining")]
public class MoneroPluginLwsIntegrationTest(ITestOutputHelper helper) : MoneroLwsIntegrationTestBase(helper)
{

    [Fact]
    public async Task ShouldLoadViewWalletOnStartUpIfExists()
    {
        await IntegrationTestUtils.CreateTestXmrWalletFilesViaRpc("");
        await IntegrationTestUtils.CloseTestXmrWalletFilesViaRpc();
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await s.RegisterNewUser(true);
        await s.CreateNewStore();
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();

        var lwsIsAvailable = await s.Page
            .Locator("li.list-group-item:text('Wallet RPC available: False')")
            .InnerTextAsync();

        Assert.Contains("Wallet RPC available: False", lwsIsAvailable);
    }
}