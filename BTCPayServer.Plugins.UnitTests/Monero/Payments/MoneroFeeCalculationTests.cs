using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Monero;
using BTCPayServer.Plugins.Monero.Payments;
using BTCPayServer.Plugins.Monero.Services;
using BTCPayServer.Services.Invoices;

using Monero.Daemon.Common;
using Monero.Wallet.Rpc;

using Moq;

using Newtonsoft.Json.Linq;

using Xunit;

namespace BTCPayServer.Plugins.UnitTests.Monero.Payments;

public class MoneroFeeCalculationTests
{
    [Fact]
    public async Task ShouldCalculateFeeFromFeeEstimate()
    {
        // Given
        var handler = CreateHandler();

        // Mainnet get_fee_estimate stub:  { "fee": 20000, "fees": [20000,80000,320000,4000000], "quantization_mask": 10000 }
        var context = CreateContext(handler,
            new GetFeeEstimateResponse
            {
                Fee = 20000,
                Fees = [20000, 80000, 320000, 4000000],
                QuantizationMask = 10000
            });

        // When
        await handler.ConfigurePrompt(context);

        // Then
        Assert.Equal(0.000030000000m, context.Prompt.PaymentMethodFee);
    }

    [Fact]
    public async Task ShouldRoundFeeUpToNonDivisibleQuantizationMask()
    {
        // Given
        var handler = CreateHandler();

        var context = CreateContext(handler,
            new GetFeeEstimateResponse { Fee = 7874, Fees = [7874, 31496, 125984, 1574800], QuantizationMask = 10000 });

        // When
        await handler.ConfigurePrompt(context);

        // Then 7874 * 1500 = 11,811,000 -> rounded up to nearest multiple of 10000 -> 11,820,000
        Assert.Equal(0.000011820000m, context.Prompt.PaymentMethodFee);
    }

    private static PaymentMethodContext CreateContext(
        MoneroLikePaymentMethodHandler handler,
        GetFeeEstimateResponse feeEstimate)
    {
        return new PaymentMethodContext(
            new StoreData(),
            new StoreBlob(),
            JObject.FromObject(new MoneroPaymentPromptDetails
            {
                AccountIndex = 0,
                InvoiceSettledConfirmationThreshold = 10
            }),
            handler,
            new InvoiceEntity { Currency = "USD" },
            new InvoiceLogs())
        {
            State = new MoneroLikePaymentMethodHandler.Prepare
            {
                GetFeeRate = Task.FromResult(feeEstimate),
                ReserveAddress =
                    s => Task.FromResult(new CreateAddressResponse { Address = "fake-xmr-address", Index = 0 }),
                AccountIndex = 0
            }
        };
    }

    private static MoneroLikePaymentMethodHandler CreateHandler()
    {
        var network = new MoneroLikeSpecificBtcPayNetwork { CryptoCode = "XMR", Divisibility = 12 };

        Mock<IMoneroRpcProvider> rpcProvider = new();
        rpcProvider.Setup(x => x.IsConfigured("XMR")).Returns(true);
        rpcProvider.Setup(x => x.IsAvailable("XMR")).Returns(true);

        return new MoneroLikePaymentMethodHandler(network, rpcProvider.Object);
    }
}