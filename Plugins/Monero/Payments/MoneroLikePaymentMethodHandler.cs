using System;
using System.Threading.Tasks;

using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Monero.Services;
using BTCPayServer.Plugins.Monero.Utils;
using BTCPayServer.Services.Invoices;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Monero.Common;
using Monero.Daemon.Common;
using Monero.Daemon.Rpc;
using Monero.Wallet.Rpc;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Monero.Payments;

public class MoneroLikePaymentMethodHandler(
    MoneroLikeSpecificBtcPayNetwork network,
    IMoneroRpcProvider moneroRpcProvider,
    ApplicationDbContextFactory applicationDbContextFactory,
    ILogger<MoneroLikePaymentMethodHandler> logger)
    : IPaymentMethodHandler
{
    public JsonSerializer Serializer { get; } = BlobSerializer.CreateSerializer().Serializer;
    public PaymentMethodId PaymentMethodId { get; } = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);

    private bool IsReady()
    {
        return moneroRpcProvider.IsConfigured(network.CryptoCode) &&
               moneroRpcProvider.IsAvailable(network.CryptoCode);
    }

    public Task BeforeFetchingRates(PaymentMethodContext context)
    {
        context.Prompt.Currency = network.CryptoCode;
        context.Prompt.Divisibility = network.Divisibility;
        if (context.Prompt.Activated && IsReady())
        {
            MoneroPaymentPromptDetails supportedPaymentMethod = ParsePaymentMethodConfig(context.PaymentMethodConfig);
            MoneroRpcConnection walletClient = moneroRpcProvider.WalletRpcClients[network.CryptoCode];
            MoneroRpcConnection daemonClient = moneroRpcProvider.DaemonRpcClients[network.CryptoCode];
            try
            {
                context.State = new Prepare
                {
                    GetFeeRate =
                        daemonClient.SendCommandAsync<GetFeeEstimateRequest, GetFeeEstimateResponse>(
                            "get_fee_estimate", new GetFeeEstimateRequest()),
                    ReserveAddress = s =>
                        walletClient.SendCommandAsync<CreateAddressRequest, CreateAddressResponse>(
                            "create_address",
                            new CreateAddressRequest
                            {
                                Label = $"btcpay invoice #{s}",
                                AccountIndex = supportedPaymentMethod.AccountIndex
                            }),
                    AccountIndex = supportedPaymentMethod.AccountIndex
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in BeforeFetchingRates");
            }
        }

        return Task.CompletedTask;
    }

    public async Task ConfigurePrompt(PaymentMethodContext context)
    {
        if (!moneroRpcProvider.IsConfigured(network.CryptoCode))
        {
            throw new PaymentMethodUnavailableException(
                "BTCPAY_XMR_WALLET_DAEMON_URI or BTCPAY_XMR_DAEMON_URI isn't configured");
        }

        if (!moneroRpcProvider.IsAvailable(network.CryptoCode) || context.State is not Prepare moneroPrepare)
        {
            throw new PaymentMethodUnavailableException("Node or wallet not available");
        }

        InvoiceEntity invoice = context.InvoiceEntity;
        GetFeeEstimateResponse feeAtomicRatePerByte = await moneroPrepare.GetFeeRate;
        CreateAddressResponse address = await moneroPrepare.ReserveAddress(invoice.Id);
        await using (ApplicationDbContext db = applicationDbContextFactory.CreateContext())
        {
            int attempts = 1;
            const int maxAttempts = 5;
            while (await db.AddressInvoices.AnyAsync(a => a.Address == address.Address))
            {
                logger.LogWarning(
                    "Subaddress collision detected for invoice {InvoiceId}: address {Address} (index {AddressIndex})" +
                    " already exists, reserving a new one (attempt {Attempt}/{MaxAttempts})",
                    invoice.Id, address.Address, address.Index, attempts, maxAttempts);

                if (++attempts > maxAttempts)
                {
                    throw new PaymentMethodUnavailableException(
                        $"Unable to reserve a unique Monero subaddress after {maxAttempts} attempts");
                }

                address = await moneroPrepare.ReserveAddress(invoice.Id);
            }
        }

        await moneroRpcProvider.StoreWallet(network.CryptoCode);

        MoneroLikeOnChainPaymentMethodDetails details = new()
        {
            AccountIndex = moneroPrepare.AccountIndex,
            AddressIndex = address.Index,
            InvoiceSettledConfirmationThreshold = ParsePaymentMethodConfig(context.PaymentMethodConfig)
                .InvoiceSettledConfirmationThreshold
        };
        context.Prompt.Destination = address.Address;
        // Multiply by 1500 bytes, which is a reasonable approximate weight of a 1-in/2-out Monero transaction.
        uint estimatedFee = feeAtomicRatePerByte.Fee * 1500;
        // Round up to the nearest multiple of QuantizationMask
        long quantizedFee = RoundUpToMask(estimatedFee, feeAtomicRatePerByte.QuantizationMask);
        context.Prompt.PaymentMethodFee = MoneroMoney.Convert(quantizedFee);
        context.Prompt.Details = JObject.FromObject(details, Serializer);
        context.TrackedDestinations.Add(address.Address);
    }

    private static long RoundUpToMask(long value, long mask)
    {
        if (mask <= 1)
        {
            return value;
        }

        long remainder = value % mask;
        return remainder == 0 ? value : value + (mask - remainder);
    }

    private MoneroPaymentPromptDetails ParsePaymentMethodConfig(JToken config)
    {
        return config.ToObject<MoneroPaymentPromptDetails>(Serializer) ??
               throw new FormatException($"Invalid {nameof(MoneroLikePaymentMethodHandler)}");
    }

    object IPaymentMethodHandler.ParsePaymentMethodConfig(JToken config)
    {
        return ParsePaymentMethodConfig(config);
    }

    public class Prepare
    {
        public Task<GetFeeEstimateResponse> GetFeeRate;
        public Func<string, Task<CreateAddressResponse>> ReserveAddress;

        public long AccountIndex { get; init; }
    }

    public MoneroLikeOnChainPaymentMethodDetails ParsePaymentPromptDetails(JToken details)
    {
        return details.ToObject<MoneroLikeOnChainPaymentMethodDetails>(Serializer);
    }

    object IPaymentMethodHandler.ParsePaymentPromptDetails(JToken details)
    {
        return ParsePaymentPromptDetails(details);
    }

    public MoneroLikePaymentData ParsePaymentDetails(JToken details)
    {
        return details.ToObject<MoneroLikePaymentData>(Serializer) ??
               throw new FormatException($"Invalid {nameof(MoneroLikePaymentMethodHandler)}");
    }

    object IPaymentMethodHandler.ParsePaymentDetails(JToken details)
    {
        return ParsePaymentDetails(details);
    }
}