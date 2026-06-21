using System.Globalization;

using BTCPayServer.Plugins.Monero.Services;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using BTCPayServer.Tests.Mocks;

using Microsoft.Playwright;

using Monero.Wallet.Common;
using Monero.Wallet.Rpc;

using Xunit;

namespace BTCPayServer.Plugins.IntegrationTests.Monero;

[Collection("Mining")]
public class MoneroPluginIntegrationTest(ITestOutputHelper helper) : MoneroIntegrationTestBase(helper)
{
    [Fact]
    public async Task ShouldEnablePluginAndPartiallyPayForInvoice()
    {
        await using var s = CreatePlaywrightTester();
        s.Server.PayTester.BindAllInterfaces = true;
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
            kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_USD"), new BidAsk(50000m)));
            kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("XMR_USD"), new BidAsk(150m)));
            kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("XMR_BTC"), new BidAsk(0.003m)));
            rateProviderFactory.Providers.Add("kraken", kraken);
        }

        await s.RegisterNewUser(true);
        await s.CreateNewStore(preferredExchange: "Kraken");
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
        await s.Page.Locator("input#PrimaryAddress")
            .FillAsync(
                "9yEzCbcYdg6MqZ5AkEh8V3YCriyN1tvmtWEHdBEUHkF6D6kN1MMD2Kd2QVWoTY67aNHNYKMUP3xfteLS2QNavJxpJdx6mWj");
        await s.Page.Locator("input#PrivateViewKey")
            .FillAsync("1f4668e8c1979b4c7dae13dc149fd95cd7ff2883becffe160c21f9e02c821c08");
        await s.Page.Locator("input#RestoreHeight").FillAsync("0");
        await s.Page.ClickAsync("button[name='command'][value='set-wallet-details']");
        var message = await s.Page
            .GetByText("View-only wallet created. The wallet will soon become available.")
            .InnerTextAsync();
        Assert.Contains("View-only wallet created", message);
        await Task.Delay(TimeSpan.FromSeconds(5),
            TestContext.Current
                .CancellationToken); // wallet-rpc needs some time to create wallet files. refactor this later

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

        // Pay half of the invoice
        await PayInvoice(s.Page, divisor: 2);

        await s.Page.GoBackAsync();
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();

        // Create a new account label
        await s.Page.FillAsync("#NewAccountLabel", "test-account");
        await s.Page.ClickAsync("button[name='command'][value='add-account']");

        // Select primary Account Index
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
        await s.Page.SelectOptionAsync("#AccountIndex", "1");
        await s.Page.ClickAsync("#SaveButton");

        // Verify selected account index
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
        var selectedValue = await s.Page.Locator("#AccountIndex").InputValueAsync();
        Assert.Equal("1", selectedValue);

        // Mine some blocks to verify
        await MiningFixture.MineToHeightOffset(8);

        // View the partially paid invoice
        await s.Page.Locator("a.nav-link[href*='invoices']").ClickAsync();
        var partialBadge = s.Page.Locator($"[data-invoice-state-badge='{invoiceId}']");
        Assert.Equal("New (paid partial) ", await partialBadge.InnerTextAsync());
        await s.Page.Locator(".invoice-details-link").ClickAsync();
        await s.Page.Locator("a.invoice-checkout-link").ClickAsync();
        await Assertions.Expect(s.Page.Locator("#PaymentInfo"))
            .ToContainTextAsync("The invoice hasn't been paid in full.");

        // Pay the invoice fully
        await PayInvoice(s.Page);

        await MiningFixture.MineToHeightOffset(7);

        // Verify amount paid on receipt
        await s.Page.Locator("#ReceiptLink").ClickAsync();
        var amountPaidSection = s.Page.Locator("#InvoiceSummary .d-flex.flex-column",
            new PageLocatorOptions { HasText = "Amount Paid" });
        await Assertions.Expect(amountPaidSection.Locator("dt.fs-2")).ToHaveTextAsync("$4.20");
    }

    private static async Task PayInvoice(IPage page, int divisor = 1)
    {
        var address = await page.Locator("#Address_XMR-CHAIN [data-text]")
            .GetAttributeAsync("data-text");
        var amountDueRaw = await page.Locator("#PaymentDetails-AmountDue dd.clipboard-button")
            .GetAttributeAsync("data-clipboard");
        var amountDueXmr = decimal.Parse(amountDueRaw!, CultureInfo.InvariantCulture);
        long amountInPiconero = (long)(amountDueXmr * 1_000_000_000_000m) / divisor;
        await GetCashCowWalletRpc().TransferAsync([
            new TransferDestination { Address = address!, Amount = amountInPiconero }
        ]);
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
            .FillAsync("1f4668e8c1979b4c7dae13dc149fd95cd7ff2883becffe160c21f9e02c821c08");
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
        await moneroRpcProvider.WalletRpcClients["XMR"]
            .SendCommandAsync<GenerateFromKeysRequest, GenerateFromKeysResponse>("generate_from_keys",
                new GenerateFromKeysRequest
                {
                    PrimaryAddress =
                        "9yEzCbcYdg6MqZ5AkEh8V3YCriyN1tvmtWEHdBEUHkF6D6kN1MMD2Kd2QVWoTY67aNHNYKMUP3xfteLS2QNavJxpJdx6mWj",
                    PrivateViewKey = "1f4668e8c1979b4c7dae13dc149fd95cd7ff2883becffe160c21f9e02c821c08",
                    WalletFileName = "wallet",
                    Password = ""
                }, TestContext.Current.CancellationToken);
        await moneroRpcProvider.CloseWallet("XMR");

        await s.RegisterNewUser(true);
        await s.CreateNewStore();
        await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
        await s.Page.Locator("input#PrimaryAddress")
            .FillAsync(
                "9yEzCbcYdg6MqZ5AkEh8V3YCriyN1tvmtWEHdBEUHkF6D6kN1MMD2Kd2QVWoTY67aNHNYKMUP3xfteLS2QNavJxpJdx6mWj");
        await s.Page.Locator("input#PrivateViewKey")
            .FillAsync("1f4668e8c1979b4c7dae13dc149fd95cd7ff2883becffe160c21f9e02c821c08");
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

    [Fact(
        Skip = "Requires container environment / proper local wallet path",
        SkipUnless = nameof(IntegrationTestUtils.RunsInContainer),
        SkipType = typeof(IntegrationTestUtils)
    )]
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