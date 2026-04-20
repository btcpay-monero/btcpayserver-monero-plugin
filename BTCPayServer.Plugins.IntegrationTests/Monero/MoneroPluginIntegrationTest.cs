using BTCPayServer.Plugins.Monero.Services;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using BTCPayServer.Tests.Mocks;

using Monero.Wallet.Rpc;

using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.IntegrationTests.Monero;

public class MoneroPluginIntegrationTest(ITestOutputHelper helper) : MoneroAndBitcoinIntegrationTestBase(helper)
{
    [Fact]
    public async Task ShouldEnableMoneroPluginSuccessfully()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        if (s.Server.PayTester.MockRates)
        {
            var rateProviderFactory = s.Server.PayTester.GetService<RateProviderFactory>();
            rateProviderFactory.Providers.Clear();

            var coinAverageMock = new MockRateProvider();
            coinAverageMock.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_USD"), new BidAsk(5000m)));
            coinAverageMock.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_EUR"), new BidAsk(4000m)));
            coinAverageMock.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("XMR_BTC"), new BidAsk(4500m)));
            rateProviderFactory.Providers.Add("coingecko", coinAverageMock);

            var kraken = new MockRateProvider();
            kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_USD"), new BidAsk(0.1m)));
            kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("XMR_USD"), new BidAsk(0.1m)));
            kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("XMR_BTC"), new BidAsk(0.1m)));
            rateProviderFactory.Providers.Add("kraken", kraken);
        }

        await s.RegisterNewUser(true);
        await s.CreateNewStore(preferredExchange: "Kraken");
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
        await s.Page.Locator("input#PrimaryAddress")
            .FillAsync(
                "43Pnj6ZKGFTJhaLhiecSFfLfr64KPJZw7MyGH73T6PTDekBBvsTAaWEUSM4bmJqDuYLizhA13jQkMRPpz9VXBCBqQQb6y5L");
        await s.Page.Locator("input#PrivateViewKey")
            .FillAsync("1bfa03b0c78aa6bc8292cf160ec9875657d61e889c41d0ebe5c54fd3a2c4b40e");
        await s.Page.Locator("input#RestoreHeight").FillAsync("0");
        await s.Page.ClickAsync("button[name='command'][value='set-wallet-details']");
        var message = await s.Page
            .GetByText("View-only wallet created. The wallet will soon become available.")
            .InnerTextAsync();
        Assert.Contains("View-only wallet created", message);
        await Task.Delay(TimeSpan.FromSeconds(5)); // wallet-rpc needs some time to create wallet files. refactor this later

        // Set rate provider
        await s.Page.Locator("#menu-item-General").ClickAsync();
        await s.Page.Locator("#menu-item-Rates").ClickAsync();
        await s.Page.FillAsync("#DefaultCurrencyPairs", "BTC_USD,XMR_USD,XMR_BTC");
        await s.Page.SelectOptionAsync("#PrimarySource_PreferredExchange", "kraken");
        await s.Page.Locator("#page-primary").ClickAsync();

        // Enable xmr wallet
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
        await s.Page.CheckAsync("#Enabled");
        await s.Page.SelectOptionAsync("#SettlementConfirmationThresholdChoice", "2");
        await s.Page.ClickAsync("#SaveButton");

        // Generate a new invoice
        await s.Page.Locator("a.nav-link[href*='invoices']").ClickAsync();
        await s.Page.Locator("#page-primary").ClickAsync();
        await s.Page.FillAsync("#Amount", "4.20");
        await s.Page.FillAsync("#BuyerEmail", "monero@monero.com");
        await Task.Delay(TimeSpan.FromSeconds(20)); // wallet-rpc needs some time to sync. refactor this later
        await s.Page.Locator("#page-primary").ClickAsync();

        // View the invoice
        var href = await s.Page.Locator("a[href^='/i/']").GetAttributeAsync("href");
        var invoiceId = href?.Split("/i/").Last();
        await s.Page.Locator($"a[href='/i/{invoiceId}']").ClickAsync();
        await s.Page.ClickAsync("#DetailsToggle");

        // Verify the total fiat amount is $4.20
        var totalFiat = await s.Page
            .Locator("#PaymentDetails-TotalFiat dd.clipboard-button")
            .InnerTextAsync();
        Assert.Equal("$4.20", totalFiat);

        await s.Page.GoBackAsync();
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();

        // Create a new account label
        await s.Page.FillAsync("#NewAccountLabel", "tst-account");
        await s.Page.ClickAsync("button[name='command'][value='add-account']");

        // Select primary Account Index
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
        await s.Page.SelectOptionAsync("#AccountIndex", "1");
        await s.Page.ClickAsync("#SaveButton");

        // Verify selected account index
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
        var selectedValue = await s.Page.Locator("#AccountIndex").InputValueAsync();
        Assert.Equal("1", selectedValue);

        // Select confirmation time to 0
        await s.Page.SelectOptionAsync("#SettlementConfirmationThresholdChoice", "3");
        await s.Page.ClickAsync("#SaveButton");
    }

    [Fact]
    public async Task ShouldFailWhenWrongPrimaryAddress()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        await s.RegisterNewUser(true);
        await s.CreateNewStore();
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
        await s.Page.Locator("input#PrimaryAddress")
            .FillAsync("wrongprimaryaddressfSF6ZKGFT7MyGH73T6PTDekBBvsTAaWEUSM4bmJqDuYLizhA13jQkMRPpz9VXBCBqQQb6y5L");
        await s.Page.Locator("input#PrivateViewKey")
            .FillAsync("1bfa03b0c78aa6bc8292cf160ec9875657d61e889c41d0ebe5c54fd3a2c4b40e");
        await s.Page.Locator("input#RestoreHeight").FillAsync("0");
        await s.Page.ClickAsync("button[name='command'][value='set-wallet-details']");
        var errorText = await s.Page
            .Locator("div.validation-summary-errors li")
            .InnerTextAsync();

        Assert.Equal("Could not generate view wallet from keys: Failed to parse public address", errorText);
    }

    [Fact]
    public async Task ShouldFailWhenWalletFileAlreadyExists()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        MoneroRpcProvider moneroRpcProvider = s.Server.PayTester.GetService<MoneroRpcProvider>();
        await moneroRpcProvider.WalletRpcClients["XMR"].SendCommandAsync<GenerateFromKeysRequest, GenerateFromKeysResponse>("generate_from_keys", new GenerateFromKeysRequest
        {
            PrimaryAddress = "43Pnj6ZKGFTJhaLhiecSFfLfr64KPJZw7MyGH73T6PTDekBBvsTAaWEUSM4bmJqDuYLizhA13jQkMRPpz9VXBCBqQQb6y5L",
            PrivateViewKey = "1bfa03b0c78aa6bc8292cf160ec9875657d61e889c41d0ebe5c54fd3a2c4b40e",
            WalletFileName = "wallet",
            Password = ""
        });
        await moneroRpcProvider.CloseWallet("XMR");

        await s.RegisterNewUser(true);
        await s.CreateNewStore();
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
        await s.Page.Locator("input#PrimaryAddress")
            .FillAsync("43Pnj6ZKGFTJhaLhiecSFfLfr64KPJZw7MyGH73T6PTDekBBvsTAaWEUSM4bmJqDuYLizhA13jQkMRPpz9VXBCBqQQb6y5L");
        await s.Page.Locator("input#PrivateViewKey")
            .FillAsync("1bfa03b0c78aa6bc8292cf160ec9875657d61e889c41d0ebe5c54fd3a2c4b40e");
        await s.Page.Locator("input#RestoreHeight").FillAsync("0");
        await s.Page.ClickAsync("button[name='command'][value='set-wallet-details']");
        var errorText = await s.Page
            .Locator("div.validation-summary-errors li")
            .InnerTextAsync();

        Assert.Equal("Could not generate view wallet from keys: Wallet already exists.", errorText);
    }

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

        var walletRpcIsAvailable = await s.Page
            .Locator("li.list-group-item:text('Wallet RPC available: True')")
            .InnerTextAsync();

        Assert.Contains("Wallet RPC available: True", walletRpcIsAvailable);
    }

    [Fact]
    public async Task ShouldLoadViewWalletWithPasswordOnStartUpIfExists()
    {
        await IntegrationTestUtils.CreateTestXmrWalletWithPasswordAsync("pass123", "wallet_password");
        await IntegrationTestUtils.CloseTestXmrWalletFilesViaRpc();
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await s.RegisterNewUser(true);
        await s.CreateNewStore();
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();

        var walletRpcIsAvailable = await s.Page
            .Locator("li.list-group-item:text('Wallet RPC available: True')")
            .InnerTextAsync();

        Assert.Contains("Wallet RPC available: True", walletRpcIsAvailable);
    }
}