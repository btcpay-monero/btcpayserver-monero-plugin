using BTCPayServer.Payments;
using BTCPayServer.Plugins.Monero.Configuration;
using BTCPayServer.Plugins.Monero.Payments;
using BTCPayServer.Plugins.Monero.RPC.Models;
using BTCPayServer.Plugins.Monero.Services;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Tests;
using BTCPayServer.Tests.Mocks;

using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.IntegrationTests.Monero;

public class MoneroPluginIntegrationTest(ITestOutputHelper helper) : MoneroAndBitcoinIntegrationTestBase(helper)
{
    #region Methods

    private const string PrimaryWalletAddress = "43Pnj6ZKGFTJhaLhiecSFfLfr64KPJZw7MyGH73T6PTDekBBvsTAaWEUSM4bmJqDuYLizhA13jQkMRPpz9VXBCBqQQb6y5L";
    private const string PrimaryViewKey = "1bfa03b0c78aa6bc8292cf160ec9875657d61e889c41d0ebe5c54fd3a2c4b40e";
    private const string SecondWalletAddress = "41u3R13W1UDhvHGNS1cqtoh4CKFkuZ5aWbkmkEskhm3SNsuaLQAeFtrXyd6Q2XAgwzf41CMC65u8fVWjB38RLAUb8AKJMw9";
    private const string SecondViewKey = "134af10334bd65ce91e015db3ea9f0b1abd1a9ed2fa378bc537498f4f52f6f0f";

    private async Task CreateWalletViaForm(
        PlaywrightTester s,
        string walletName,
        string address,
        string viewKey,
        string restoreHeight)
    {
        await s.Page.FillAsync("input#WalletName", walletName);
        await s.Page.FillAsync("input#PrimaryAddress", address);
        await s.Page.FillAsync("input#PrivateViewKey", viewKey);
        await s.Page.FillAsync("input#RestoreHeight", restoreHeight);
        await s.Page.ClickAsync("button[name='command'][value='connect-wallet']");
        await s.Page.Locator($".wallet-card[data-wallet='{address}']").WaitForAsync();
    }

    private async Task CreateWalletViaModal(
        PlaywrightTester s,
        string walletName,
        string address,
        string viewKey,
        string restoreHeight)
    {
        await s.Page.FillAsync("#createWalletModal input#WalletName", walletName);
        await s.Page.FillAsync("#createWalletModal input#PrimaryAddress", address);
        await s.Page.FillAsync("#createWalletModal input#PrivateViewKey", viewKey);
        await s.Page.FillAsync("#createWalletModal input#RestoreHeight", restoreHeight);
        await s.Page.ClickAsync("#createWalletModal button[name='command'][value='add-wallet']");
        await s.Page.Locator($".wallet-card[data-wallet='{address}']").WaitForAsync();
    }

    private async Task<bool> WalletExists(PlaywrightTester s, string walletAddress)
    {
        var walletCard = s.Page.Locator($".wallet-card[data-wallet='{walletAddress}']");
        try
        {
            await walletCard.WaitForAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task SwitchActiveWallet(PlaywrightTester s, string walletAddress)
    {
        var walletCard = s.Page.Locator($".wallet-card[data-wallet='{walletAddress}']");
        await walletCard.Locator(".wallet-card-content").ClickAsync();
        await s.Page.Locator("#switchWalletModal").WaitForAsync();
        await s.Page.ClickAsync("button[name='command'][value='set-active-wallet']");
        await s.Page.Locator($".wallet-card[data-wallet='{walletAddress}'].wallet-card-active").WaitForAsync();
    }

    private async Task DeleteWallet(PlaywrightTester s, string walletAddress)
    {
        var walletCard = s.Page.Locator($".wallet-card[data-wallet='{walletAddress}']");
        await walletCard.Locator(".wallet-actions .dropdown-toggle").ClickAsync();
        await walletCard.Locator(".wallet-action-delete").ClickAsync();
        await s.Page.Locator("#deleteWalletModal").WaitForAsync();
        await s.Page.Locator("#deleteWalletModal button.btn-danger").ClickAsync();
        await s.Page.Locator(".alert-success:has-text('has been deleted')").WaitForAsync();
    }

    private async Task<bool> WalletIsActive(PlaywrightTester s, string walletAddress)
    {
        var walletCard = s.Page.Locator($".wallet-card[data-wallet='{walletAddress}']");
        var hasActiveClass = await walletCard.GetAttributeAsync("class");
        return hasActiveClass?.Contains("wallet-card-active") ?? false;
    }

    #endregion

    //This test tracks the duplicate address issue
    [Fact]
    public async Task CreateInvoiceDeleteAndReimportWallet()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        try
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
                kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_USD"), new BidAsk(0.1m)));
                kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("XMR_USD"), new BidAsk(0.1m)));
                kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("XMR_BTC"), new BidAsk(0.1m)));
                rateProviderFactory.Providers.Add("kraken", kraken);
            }

            await s.RegisterNewUser(true);
            await s.CreateNewStore(preferredExchange: "Kraken");
            await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
            await s.Page.Locator("text=No Wallet Configured").WaitForAsync();
            await s.Page.ClickAsync("a[href*='/connect/XMR']");
            await s.Page.Locator("input#WalletName").WaitForAsync();
            await CreateWalletViaForm(
                s,
                walletName: "primary-wallet",
                address: PrimaryWalletAddress,
                viewKey: PrimaryViewKey,
                restoreHeight: "0"
            );
            Assert.True(await WalletExists(s, PrimaryWalletAddress));
            Assert.True(await WalletIsActive(s, PrimaryWalletAddress));

            await s.Page.CheckAsync("#Enabled");
            await s.Page.SelectOptionAsync("#SettlementConfirmationThresholdChoice", "2");
            await s.Page.ClickAsync("#SaveButton");
            await s.Page.Locator("svg.icon-checkmark.text-success").WaitForAsync();

            var invoiceId = await s.CreateInvoice(s.StoreId, 10, "USD");
            await s.GoToInvoiceCheckout(invoiceId);

            var copyButton = s.Page.Locator("button[data-clipboard].clipboard-button");
            var moneroAddress = await copyButton.GetAttributeAsync("data-clipboard");
            Assert.NotNull(moneroAddress);
            Assert.True(moneroAddress.StartsWith('8'), $"Expected Monero address to start with 8, but got: {moneroAddress}");

            await s.GoToStore(s.StoreId);
            await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
            Assert.True(await WalletIsActive(s, PrimaryWalletAddress));

            await s.Page.CheckAsync("#Enabled");
            await s.Page.SelectOptionAsync("#SettlementConfirmationThresholdChoice", "3");
            await s.Page.ClickAsync("#SaveButton");

            await s.GoToStore(s.StoreId);
            await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
            await s.Page.Locator("#walletManagementMenu").WaitForAsync();

            Assert.True(await WalletExists(s, PrimaryWalletAddress));
            Assert.True(await WalletIsActive(s, PrimaryWalletAddress));

            await DeleteWallet(s, PrimaryWalletAddress);
            await s.Page.ClickAsync("a#ConnectWalletLink");
            await s.Page.Locator("input#WalletName").WaitForAsync();
            await CreateWalletViaForm(
                s,
                walletName: "reimport-wallet",
                address: PrimaryWalletAddress,
                viewKey: PrimaryViewKey,
                restoreHeight: "0"
            );
            Assert.True(await WalletExists(s, PrimaryWalletAddress));
            Assert.True(await WalletIsActive(s, PrimaryWalletAddress));

            await s.Page.CheckAsync("#Enabled");
            await s.Page.SelectOptionAsync("#SettlementConfirmationThresholdChoice", "2");
            await s.Page.ClickAsync("#SaveButton");
            await s.Page.Locator("svg.icon-checkmark.text-success").WaitForAsync();

            //Uncomment after implementing wallet index tracking
            // var invoiceId2 = await s.CreateInvoice(s.StoreId, 10, "USD");
            // await s.GoToInvoiceCheckout(invoiceId2);
        }
        finally
        {
            await IntegrationTestUtils.CleanUpAsync(s);
        }
    }

    [Fact]
    public async Task DuplicateWalletShouldFail()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        try
        {
            await s.RegisterNewUser(true);
            await s.CreateNewStore();

            await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
            await s.Page.Locator("text=No Wallet Configured").WaitForAsync();
            await s.Page.ClickAsync("a[href*='/connect/XMR']");
            await s.Page.Locator("input#WalletName").WaitForAsync();

            await CreateWalletViaForm(
                s,
                walletName: "first-wallet",
                address: PrimaryWalletAddress,
                viewKey: PrimaryViewKey,
                restoreHeight: "0"
            );

            Assert.True(await WalletExists(s, PrimaryWalletAddress));
            Assert.True(await WalletIsActive(s, PrimaryWalletAddress));

            await s.Page.ClickAsync("#walletManagementMenu");
            await s.Page.ClickAsync("button[data-bs-target='#createWalletModal']");

            var modal = s.Page.Locator("#createWalletModal");
            await modal.WaitForAsync();

            await modal.Locator("input#WalletName").FillAsync("duplicate-wallet");
            await modal.Locator("input#PrimaryAddress").FillAsync(PrimaryWalletAddress);
            await modal.Locator("input#PrivateViewKey").FillAsync(PrimaryViewKey);
            await modal.Locator("input#RestoreHeight").FillAsync("0");
            await modal.Locator("button[name='command'][value='add-wallet']").ClickAsync();

            var validationSummary = s.Page.Locator("div.validation-summary-errors, div[asp-validation-summary]");
            var errorText = await validationSummary.TextContentAsync();
            Assert.Contains("this wallet is already imported as first-wallet. a wallet cannot be imported more than once.", errorText?.ToLower() ?? string.Empty);
            Assert.True(await WalletIsActive(s, PrimaryWalletAddress));
        }
        finally
        {
            await IntegrationTestUtils.CleanUpAsync(s);
        }
    }

    [Fact]
    public async Task CompleteMigratePasswordFromFile()
    {
        const string walletName = "wallet";
        const string walletPassword = "legacy-password-123";
        string passwordFile;

        {
            await using var tempTester = CreatePlaywrightTester();
            await tempTester.StartAsync();
            var rpcProvider = tempTester.Server.PayTester.GetService<MoneroRpcProvider>();
            var xmrWallet = await rpcProvider.CreateWalletFromKeys(
                "XMR",
                walletName,
                PrimaryWalletAddress,
                PrimaryViewKey,
                walletPassword,
                0);

            Assert.True(xmrWallet.Success);
            await rpcProvider.CloseWallet("XMR");
            string walletDir = rpcProvider.GetWalletDirectory("XMR");
            Assert.NotNull(walletDir);
            passwordFile = Path.Combine(walletDir, "password");
            await File.WriteAllTextAsync(passwordFile, walletPassword);

            await IntegrationTestUtils.CleanUpAsync(tempTester, deleteWalletFiles: false);
        }

        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        try
        {
            var walletService = s.Server.PayTester.GetService<MoneroWalletService>();
            MoneroWalletState walletState = walletService.GetWalletState();
            Assert.Equal(walletName, walletState.ActiveWalletName);
            Assert.True(walletState.IsConnected);
            await s.RegisterNewUser(true);
            await s.CreateNewStore();
            await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();

            var walletCard = s.Page.Locator($".wallet-card[data-wallet='{PrimaryWalletAddress}']");
            await walletCard.WaitForAsync();
            Assert.True(await WalletExists(s, PrimaryWalletAddress));
            Assert.True(await WalletIsActive(s, PrimaryWalletAddress));

            var deprecatedWarning = s.Page.Locator(".alert-warning:has-text('Deprecated password file detected')");
            await deprecatedWarning.WaitForAsync();
            Assert.True(await deprecatedWarning.IsVisibleAsync());
        }
        finally
        {
            await IntegrationTestUtils.CleanUpAsync(s);
            File.Delete(passwordFile);
        }
    }

    [Fact]
    public async Task ShouldEnableMoneroPluginSuccessfully()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        try
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
                kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_USD"), new BidAsk(0.1m)));
                kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("XMR_USD"), new BidAsk(0.1m)));
                kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("XMR_BTC"), new BidAsk(0.1m)));
                rateProviderFactory.Providers.Add("kraken", kraken);
            }

            await s.RegisterNewUser(true);
            await s.CreateNewStore(preferredExchange: "Kraken");
            await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
            await s.Page.Locator("text=No Wallet Configured").WaitForAsync();
            await s.Page.ClickAsync("a[href*='/connect/XMR']");
            await s.Page.Locator("input#WalletName").WaitForAsync();
            await CreateWalletViaForm(
                s,
                walletName: "primary-wallet",
                address: PrimaryWalletAddress,
                viewKey: PrimaryViewKey,
                restoreHeight: "0"
            );
            Assert.True(await WalletExists(s, PrimaryWalletAddress));
            Assert.True(await WalletIsActive(s, PrimaryWalletAddress));

            await s.Page.CheckAsync("#Enabled");
            await s.Page.SelectOptionAsync("#SettlementConfirmationThresholdChoice", "2");
            await s.Page.ClickAsync("#SaveButton");
            await s.Page.Locator("svg.icon-checkmark.text-success").WaitForAsync();

            var invoiceId = await s.CreateInvoice(s.StoreId, 10, "USD");
            await s.GoToInvoiceCheckout(invoiceId);

            var copyButton = s.Page.Locator("button[data-clipboard].clipboard-button");
            var moneroAddress = await copyButton.GetAttributeAsync("data-clipboard");
            Assert.NotNull(moneroAddress);
            Assert.True(moneroAddress.StartsWith('8'), $"Expected Monero address to start with 8, but got: {moneroAddress}");

            await s.GoToStore(s.StoreId);
            await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
            Assert.True(await WalletIsActive(s, PrimaryWalletAddress));

            await s.Page.CheckAsync("#Enabled");
            await s.Page.SelectOptionAsync("#SettlementConfirmationThresholdChoice", "3");
            await s.Page.ClickAsync("#SaveButton");

            await s.GoToStore(s.StoreId);
            await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
            await s.Page.Locator("#walletManagementMenu").WaitForAsync();

            Assert.True(await WalletExists(s, PrimaryWalletAddress));
            Assert.True(await WalletIsActive(s, PrimaryWalletAddress));
            await s.Page.ClickAsync("#walletManagementMenu");
            await s.Page.ClickAsync("button[data-bs-target='#createWalletModal']");
            await s.Page.Locator("#createWalletModal input#WalletName").WaitForAsync();

            await CreateWalletViaModal(
                s,
                walletName: "second-wallet",
                address: SecondWalletAddress,
                viewKey: SecondViewKey,
                restoreHeight: "100"
            );
            Assert.True(await WalletExists(s, PrimaryWalletAddress));
            Assert.True(await WalletExists(s, SecondWalletAddress));
            Assert.True(await WalletIsActive(s, SecondWalletAddress));

            await SwitchActiveWallet(s, PrimaryWalletAddress);
            Assert.True(await WalletIsActive(s, PrimaryWalletAddress));
            Assert.False(await WalletIsActive(s, SecondWalletAddress));

            await DeleteWallet(s, SecondWalletAddress);
            Assert.True(await WalletExists(s, PrimaryWalletAddress));
            Assert.False(await WalletExists(s, SecondWalletAddress));
            Assert.True(await WalletIsActive(s, PrimaryWalletAddress));
        }
        finally
        {
            await IntegrationTestUtils.CleanUpAsync(s);
        }
    }

    [Fact]
    public async Task NewWalletFirstInvoiceGetsAddressIndexOne()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        try
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
                kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("BTC_USD"), new BidAsk(0.1m)));
                kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("XMR_USD"), new BidAsk(0.1m)));
                kraken.ExchangeRates.Add(new PairRate(CurrencyPair.Parse("XMR_BTC"), new BidAsk(0.1m)));
                rateProviderFactory.Providers.Add("kraken", kraken);
            }
            await s.RegisterNewUser(true);
            await s.CreateNewStore();

            await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
            await s.Page.Locator("text=No Wallet Configured").WaitForAsync();
            await s.Page.ClickAsync("a[href*='/connect/XMR']");
            await s.Page.Locator("input#WalletName").WaitForAsync();

            await CreateWalletViaForm(
                s,
                walletName: "fresh-wallet",
                address: PrimaryWalletAddress,
                viewKey: PrimaryViewKey,
                restoreHeight: "0"
            );

            await s.Page.CheckAsync("#Enabled");
            await s.Page.SelectOptionAsync("#SettlementConfirmationThresholdChoice", "2");
            await s.Page.ClickAsync("#SaveButton");
            await s.Page.Locator("svg.icon-checkmark.text-success").WaitForAsync();

            var invoiceId = await s.CreateInvoice(s.StoreId, 10, "USD");

            var invoiceRepository = s.Server.PayTester.GetService<InvoiceRepository>();
            var invoice = await invoiceRepository.GetInvoice(invoiceId);

            var xmrPaymentPrompt = invoice.GetPaymentPrompt(PaymentTypes.CHAIN.GetPaymentMethodId("XMR"));
            Assert.NotNull(xmrPaymentPrompt);

            var details = xmrPaymentPrompt.Details.ToObject<MoneroLikeOnChainPaymentMethodDetails>();
            Assert.NotNull(details);

            Assert.Equal(0, details.AccountIndex);
            Assert.Equal(1, details.AddressIndex);

            await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
            await DeleteWallet(s, PrimaryWalletAddress);
            Assert.False(await WalletExists(s, PrimaryWalletAddress));
        }
        finally
        {
            await IntegrationTestUtils.CleanUpAsync(s);
        }
    }

    [Fact]
    public async Task ShouldFailWhenWrongPrimaryAddress()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        try
        {
            await s.RegisterNewUser(true);
            await s.CreateNewStore();
            await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
            await s.Page.Locator("text=No Wallet Configured").WaitForAsync();
            await s.Page.ClickAsync("a[href*='/connect/XMR']");
            await s.Page.Locator("input#WalletName").WaitForAsync();
            await s.Page.FillAsync("input#WalletName", "wrong-wallet");
            await s.Page.FillAsync("input#PrimaryAddress", "wrongprimaryaddressfSF6ZKGFT7MyGH73T6PTDekBBvsTAaWEUSM4bmJqDuYLizhA13jQkMRPpz9VXBCBqQQb6y5L");
            await s.Page.FillAsync("input#PrivateViewKey", PrimaryViewKey);
            await s.Page.FillAsync("input#RestoreHeight", "0");
            await s.Page.ClickAsync("button[name='command'][value='connect-wallet']");
            var errorLocator = s.Page.Locator("div.validation-summary-errors li");
            await errorLocator.WaitForAsync();
            var errorText = await errorLocator.InnerTextAsync();

            Assert.Equal("Could not create wallet: Failed to parse public address", errorText);
        }
        finally
        {
            await IntegrationTestUtils.CleanUpAsync(s);
        }
    }

    [Fact]
    public async Task ShouldFailWhenWalletFileAlreadyExists()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();

        try
        {
            MoneroRpcProvider moneroRpcProvider = s.Server.PayTester.GetService<MoneroRpcProvider>();
            await moneroRpcProvider.WalletRpcClients["XMR"].SendCommandAsync<GenerateFromKeysRequest, GenerateFromKeysResponse>("generate_from_keys", new GenerateFromKeysRequest
            {
                PrimaryAddress = PrimaryWalletAddress,
                PrivateViewKey = PrimaryViewKey,
                WalletFileName = "duplicate-wallet",
                Password = "monero"
            });
            await moneroRpcProvider.CloseWallet("XMR");

            await s.RegisterNewUser(true);
            await s.CreateNewStore();
            await s.Page.Locator("a.nav-link[href*='monerolike/XMR']").ClickAsync();
            await s.Page.Locator("text=No Wallet Configured").WaitForAsync();
            await s.Page.ClickAsync("a[href*='/connect/XMR']");
            await s.Page.Locator("input#WalletName").WaitForAsync();

            await s.Page.FillAsync("input#WalletName", "duplicate-wallet");
            await s.Page.FillAsync("input#PrimaryAddress", PrimaryWalletAddress);
            await s.Page.FillAsync("input#PrivateViewKey", PrimaryViewKey);
            await s.Page.FillAsync("input#RestoreHeight", "0");
            await s.Page.ClickAsync("button[name='command'][value='connect-wallet']");

            var errorText = await s.Page.Locator("div.validation-summary-errors li").InnerTextAsync();
            Assert.Contains("already exists", errorText.ToLowerInvariant());
        }
        finally
        {
            await IntegrationTestUtils.CleanUpAsync(s);
        }
    }
}