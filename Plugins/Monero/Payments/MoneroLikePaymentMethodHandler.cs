using System;
using System.Threading.Tasks;

using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Monero.RPC.Models;
using BTCPayServer.Plugins.Monero.Services;
using BTCPayServer.Plugins.Monero.Utils;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Monero.Payments
{
    public class MoneroLikePaymentMethodHandler : IPaymentMethodHandler
    {
        private readonly MoneroLikeSpecificBtcPayNetwork _network;
        public MoneroLikeSpecificBtcPayNetwork Network => _network;
        public JsonSerializer Serializer { get; }
        private readonly MoneroRPCProvider _moneroRpcProvider;

        public PaymentMethodId PaymentMethodId { get; }

        public MoneroLikePaymentMethodHandler(MoneroLikeSpecificBtcPayNetwork network, MoneroRPCProvider moneroRpcProvider)
        {
            PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            _network = network;
            Serializer = BlobSerializer.CreateSerializer().Serializer;
            _moneroRpcProvider = moneroRpcProvider;
        }
        bool IsReady() => _moneroRpcProvider.IsConfigured(_network.CryptoCode) && _moneroRpcProvider.IsAvailable(_network.CryptoCode);

        public Task BeforeFetchingRates(PaymentMethodContext context)
        {
            context.Prompt.Currency = _network.CryptoCode;
            context.Prompt.Divisibility = _network.Divisibility;
            if (context.Prompt.Activated && IsReady())
            {
                var supportedPaymentMethod = ParsePaymentMethodConfig(context.PaymentMethodConfig);
                var walletClient = _moneroRpcProvider.WalletRpcClients[_network.CryptoCode];
                var daemonClient = _moneroRpcProvider.DaemonRpcClients[_network.CryptoCode];
                try
                {
                    context.State = new Prepare()
                    {
                        GetFeeRate = daemonClient.SendCommandAsync<GetFeeEstimateRequest, GetFeeEstimateResponse>("get_fee_estimate", new GetFeeEstimateRequest()),
                        ReserveAddress = s => walletClient.SendCommandAsync<CreateAddressRequest, CreateAddressResponse>("create_address", new CreateAddressRequest() { Label = $"btcpay invoice #{s}", AccountIndex = supportedPaymentMethod.AccountIndex }),
                        AccountIndex = supportedPaymentMethod.AccountIndex
                    };
                }
                catch (Exception ex)
                {
                    context.Logs.Write($"Error in BeforeFetchingRates: {ex.Message}", InvoiceEventData.EventSeverity.Error);
                }
            }
            return Task.CompletedTask;
        }

        public async Task ConfigurePrompt(PaymentMethodContext context)
        {
            if (!_moneroRpcProvider.IsConfigured(_network.CryptoCode))
            {
                throw new PaymentMethodUnavailableException($"BTCPAY_XMR_WALLET_DAEMON_URI or BTCPAY_XMR_DAEMON_URI isn't configured");
            }

            if (!_moneroRpcProvider.IsAvailable(_network.CryptoCode) || context.State is not Prepare moneroPrepare)
            {
                throw new PaymentMethodUnavailableException($"Node or wallet not available");
            }

            var invoice = context.InvoiceEntity;
            var feeRatePerKb = await moneroPrepare.GetFeeRate;
            var address = await moneroPrepare.ReserveAddress(invoice.Id);

            var feeRatePerByte = feeRatePerKb.Fee / 1024;
            var details = new MoneroLikeOnChainPaymentMethodDetails()
            {
                AccountIndex = moneroPrepare.AccountIndex,
                AddressIndex = address.AddressIndex,
                InvoiceSettledConfirmationThreshold = ParsePaymentMethodConfig(context.PaymentMethodConfig).InvoiceSettledConfirmationThreshold
            };
            context.Prompt.Destination = address.Address;
            context.Prompt.PaymentMethodFee = MoneroMoney.Convert(feeRatePerByte * 100);
            context.Prompt.Details = JObject.FromObject(details, Serializer);
            context.TrackedDestinations.Add(address.Address);
        }
        private MoneroPaymentPromptDetails ParsePaymentMethodConfig(JToken config)
        {
            return config.ToObject<MoneroPaymentPromptDetails>(Serializer) ?? throw new FormatException($"Invalid {nameof(MoneroLikePaymentMethodHandler)}");
        }
        object IPaymentMethodHandler.ParsePaymentMethodConfig(JToken config)
        {
            return ParsePaymentMethodConfig(config);
        }

        class Prepare
        {
            public Task<GetFeeEstimateResponse> GetFeeRate;
            public Func<string, Task<CreateAddressResponse>> ReserveAddress;

            public long AccountIndex { get; internal set; }
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
            return details.ToObject<MoneroLikePaymentData>(Serializer) ?? throw new FormatException($"Invalid {nameof(MoneroLikePaymentMethodHandler)}");
        }
        object IPaymentMethodHandler.ParsePaymentDetails(JToken details)
        {
            return ParsePaymentDetails(details);
        }
    }
}