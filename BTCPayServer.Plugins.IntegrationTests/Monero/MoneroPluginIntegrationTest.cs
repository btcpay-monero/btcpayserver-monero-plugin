using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using BTCPayServer.Tests.Mocks;

using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.IntegrationTests.Monero;

public class MoneroPluginIntegrationTest(ITestOutputHelper helper) : MoneroAndBitcoinIntegrationTestBase(helper)
{
    [Fact]
    public async Task EnableMoneroPluginSuccessfully()
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
        await s.Page.CheckAsync("#Enabled");
        await s.Page.SelectOptionAsync("#SettlementConfirmationThresholdChoice", "2");
        await s.Page.ClickAsync("#SaveButton");
        var classList = await s.Page.Locator("svg.icon-checkmark").GetAttributeAsync("class");
        Assert.Contains("text-success", classList);

        // Set rate provider
        await s.Page.Locator("#StoreNav-General").ClickAsync();
        await s.Page.Locator("#mainNav #StoreNav-Rates").ClickAsync();
        await s.Page.FillAsync("#DefaultCurrencyPairs", "BTC_USD,XMR_USD,XMR_BTC");
        await s.Page.SelectOptionAsync("#PrimarySource_PreferredExchange", "kraken");
        await s.Page.Locator("#page-primary").ClickAsync();

        // Generate a new invoice
        await s.Page.Locator("a.nav-link[href*='invoices']").ClickAsync();
        await s.Page.Locator("#page-primary").ClickAsync();
        await s.Page.FillAsync("#Amount", "4.20");
        await s.Page.FillAsync("#BuyerEmail", "monero@monero.com");
        await Task.Delay(TimeSpan.FromSeconds(25)); // wallet-rpc needs some time to sync. refactor this later
        await s.Page.Locator("#page-primary").ClickAsync();

        // View the invoice
        var href = await s.Page.Locator("a[href^='/i/']").GetAttributeAsync("href");
        var invoiceId = href?.Split("/i/").Last();
        await s.Page.Locator($"a[href='/i/{invoiceId}']").ClickAsync();
        await s.Page.ClickAsync("#DetailsToggle");

        // Verify the total fiat amount is $4.20
        var totalFiat = await s.Page.Locator("#PaymentDetails-TotalFiat dd.clipboard-button").InnerTextAsync();
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
}