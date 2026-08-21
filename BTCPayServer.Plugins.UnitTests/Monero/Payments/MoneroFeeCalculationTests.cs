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
    // Mainnet get_fee_estimate stub from the RPC example:
    // { "fee": 20000, "fees": [20000,80000,320000,4000000], "quantization_mask": 10000 }
    private static GetFeeEstimateResponse MainnetFeeStub() => new()
    {
        Fee = 20000,
        Fees = [20000, 80000, 320000, 4000000],
        QuantizationMask = 10000
    };

    [Fact]
    public async Task ConfigurePrompt_ShouldConfigurePromptWithReservedAddress()
    {
        // Given
        MoneroLikeSpecificBtcPayNetwork network = new() { CryptoCode = "XMR", Divisibility = 12 };

        Mock<IMoneroRpcProvider> rpcProvider = new();
        rpcProvider.Setup(x => x.IsConfigured("XMR")).Returns(true);
        rpcProvider.Setup(x => x.IsAvailable("XMR")).Returns(true);

        MoneroLikePaymentMethodHandler handler = new(network, rpcProvider.Object);

        var context = new PaymentMethodContext(
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
                GetFeeRate = Task.FromResult(MainnetFeeStub()),
                ReserveAddress =
                    s => Task.FromResult(new CreateAddressResponse { Address = "fake-xmr-address", Index = 0 }),
                AccountIndex = 0
            }
        };

        // When
        await handler.ConfigurePrompt(context);
        // Then fees are 1.9E-09 XMR
        Assert.Equal(0.000000001900m, context.Prompt.PaymentMethodFee);
    }
}