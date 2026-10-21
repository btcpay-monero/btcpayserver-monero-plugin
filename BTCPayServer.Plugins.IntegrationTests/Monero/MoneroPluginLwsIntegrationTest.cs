using Xunit;

namespace BTCPayServer.Plugins.IntegrationTests.Monero;

[Collection("Mining")]
public class MoneroPluginLwsIntegrationTest(ITestOutputHelper helper) : MoneroLwsIntegrationTestBase(helper)
{

    [Fact]
    public async Task ShouldLoadViewWalletOnStartUpIfExists()
    {
        const string primaryAddress =
            "9vGsWQCtxCWBXQBsmiRvPpgzNw6CL5ANqLip4UH8xXW7FrszpZTeAQdPxDp5H1vPnWa6UxFovxqqGBZQUnZ9i9GFBir3eHV";
        const string privateViewKey = "31d082a3fab955ec6abe83d0edbfae7a0fbae66358230ee3eb01c155c32e880d";
        await IntegrationTestUtils.CreateTestXmrWalletFilesViaRpc(primaryAddress, privateViewKey, "");
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