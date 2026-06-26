using System.Globalization;

using BTCPayServer.Plugins.Monero.Services;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using BTCPayServer.Tests;
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
    public async Task ShouldSettleInvoiceAfterPartialThenFullPayment()
    {
        await using var s = CreatePlaywrightTester();
        s.Server.PayTester.BindAllInterfaces = true;
        await s.StartAsync();

        var invoiceId = await SetupStoreWithXmrAndCreateInvoice(s, amount: "4.20");

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

        await AssertPartialPaymentState(s.Page, invoiceId);

        // Pay the invoice fully
        await PayInvoice(s.Page);

        await MiningFixture.MineToHeightOffset(7);

        await AssertFullyPaidReceipt(s.Page, "$4.20");
    }

    [Fact]
    public async Task ShouldReuseOriginalAddressForSubsequentPartialPayment()
    {
        await using var s = CreatePlaywrightTester();
        s.Server.PayTester.BindAllInterfaces = true;
        await s.StartAsync();

        var invoiceId = await SetupStoreWithXmrAndCreateInvoice(s, amount: "4.20");

        // Pay half of the invoice
        (decimal halfOfTheOriginalPay, string originalAddress) = await PayInvoice(s.Page, divisor: 2);

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

        await AssertPartialPaymentState(s.Page, invoiceId);

        // Pay the invoice with original half
        await PayInvoice(s.Page, 1, halfOfTheOriginalPay, originalAddress);

        await MiningFixture.MineToHeightOffset(7);

        // Pay the invoice fully
        await PayInvoice(s.Page);

        // Mine blocks to verify
        await MiningFixture.MineToHeightOffset(6);

        await s.Page.Locator("#ReceiptLink").ClickAsync();

        // Second payment should reuse original address
        var destinationCells = s.Page.Locator("#PaymentDetails table tbody tr td:first-child .truncate-center-text");
        var destinations = await destinationCells.AllInnerTextsAsync();
        Assert.Equal(3, destinations.Count);
        Assert.Equal(originalAddress, destinations[0]);
        Assert.Equal(originalAddress, destinations[1]);
    }

    private static async Task<string> SetupStoreWithXmrAndCreateInvoice(PlaywrightTester s, string amount)
    {
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
        await s.Page.FillAsync("#Amount", amount);
        await s.Page.FillAsync("#BuyerEmail", "monero@monero.com");
        await s.Page.Locator("#page-primary").ClickAsync();

        // View the invoice
        var href = await s.Page.Locator("a[href^='/i/']").GetAttributeAsync("href");
        var invoiceId = href?.Split("/i/").Last()!;
        await s.Page.Locator($"a[href='/i/{invoiceId}']").ClickAsync();
        await s.Page.ClickAsync("#DetailsToggle");

        // Verify the total fiat amount
        var totalFiat = await s.Page
            .Locator("#PaymentDetails-TotalFiat dd.clipboard-button")
            .InnerTextAsync();
        Assert.Equal($"${amount}", totalFiat);

        return invoiceId;
    }

    private static async Task AssertPartialPaymentState(IPage page, string invoiceId)
    {
        await page.Locator("a.nav-link[href*='invoices']").ClickAsync();
        var partialBadge = page.Locator($"[data-invoice-state-badge='{invoiceId}']");
        await Assertions.Expect(partialBadge).ToContainTextAsync("New (paid partial) ");
        await page.Locator(".invoice-details-link").ClickAsync();
        await page.Locator("a.invoice-checkout-link").ClickAsync();
        await Assertions.Expect(page.Locator("#PaymentInfo"))
            .ToContainTextAsync("The invoice hasn't been paid in full.");
    }

    private static async Task AssertFullyPaidReceipt(IPage page, string expectedFiatAmount)
    {
        await page.Locator("#ReceiptLink").ClickAsync();
        var amountPaidSection = page.Locator("#InvoiceSummary .d-flex.flex-column",
            new PageLocatorOptions { HasText = "Amount Paid" });
        await Assertions.Expect(amountPaidSection.Locator("dt.fs-2")).ToHaveTextAsync(expectedFiatAmount);
    }

    private static async Task<(decimal AmountPaid, string Address)> PayInvoice(IPage page, int divisor = 1,
        decimal? amountDueXmr = null, string? address = null)
    {
        address ??= await page.Locator("#Address_XMR-CHAIN [data-text]")
            .GetAttributeAsync("data-text");
        if (amountDueXmr is null)
        {
            var raw = await page.Locator("#PaymentDetails-AmountDue dd.clipboard-button")
                .GetAttributeAsync("data-clipboard");
            amountDueXmr = decimal.Parse(raw!, CultureInfo.InvariantCulture);
        }
        long piconero = (long)(amountDueXmr.Value * 1_000_000_000_000m) / divisor;
        await GetCashCowWalletRpc().TransferAsync([
            new TransferDestination { Address = address!, Amount = piconero }
        ]);
        return (piconero / 1_000_000_000_000m, address!);
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