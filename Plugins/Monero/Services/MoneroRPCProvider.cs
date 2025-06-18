using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using BTCPayServer.Plugins.Monero.Configuration;
using BTCPayServer.Plugins.Monero.RPC;
using BTCPayServer.Plugins.Monero.RPC.Models;
using BTCPayServer.Services;

using Microsoft.Extensions.Logging;

using NBitcoin;

namespace BTCPayServer.Plugins.Monero.Services
{
    public class MoneroRPCProvider
    {
        private readonly MoneroLikeConfiguration _moneroLikeConfiguration;
        private readonly ILogger<MoneroRPCProvider> _logger;
        private readonly EventAggregator _eventAggregator;
        private readonly BTCPayServerEnvironment environment;
        public ImmutableDictionary<string, JsonRpcClient> DaemonRpcClients;
        public ImmutableDictionary<string, JsonRpcClient> WalletRpcClients;

        private readonly ConcurrentDictionary<string, MoneroLikeSummary> _summaries = new();

        public ConcurrentDictionary<string, MoneroLikeSummary> Summaries => _summaries;

        public MoneroRPCProvider(MoneroLikeConfiguration moneroLikeConfiguration,
            ILogger<MoneroRPCProvider> logger,
            EventAggregator eventAggregator,
            IHttpClientFactory httpClientFactory, BTCPayServerEnvironment environment)
        {
            _moneroLikeConfiguration = moneroLikeConfiguration;
            _logger = logger;
            _eventAggregator = eventAggregator;
            this.environment = environment;
            DaemonRpcClients =
                _moneroLikeConfiguration.MoneroLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new JsonRpcClient(pair.Value.DaemonRpcUri, pair.Value.Username, pair.Value.Password,
                        httpClientFactory.CreateClient($"{pair.Key}client")));
            WalletRpcClients =
                _moneroLikeConfiguration.MoneroLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new JsonRpcClient(pair.Value.InternalWalletRpcUri, "", "",
                        httpClientFactory.CreateClient($"{pair.Key}client")));
            if (environment.CheatMode)
            {
                CashCowWalletRpcClients =
                    _moneroLikeConfiguration.MoneroLikeConfigurationItems
                        .Where(i => i.Value.CashCowWalletRpcUri is not null).ToImmutableDictionary(pair => pair.Key,
                            pair => new JsonRpcClient(pair.Value.CashCowWalletRpcUri, "", "",
                                httpClientFactory.CreateClient($"{pair.Key}cashcow-client")));
            }
        }

        public ImmutableDictionary<string, JsonRpcClient> CashCowWalletRpcClients { get; set; }

        public bool IsConfigured(string cryptoCode) => WalletRpcClients.ContainsKey(cryptoCode) && DaemonRpcClients.ContainsKey(cryptoCode);
        public bool IsAvailable(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            return _summaries.ContainsKey(cryptoCode) && IsAvailable(_summaries[cryptoCode]);
        }

        private bool IsAvailable(MoneroLikeSummary summary)
        {
            return summary.Synced &&
                   summary.WalletAvailable;
        }

        public async Task<MoneroLikeSummary> UpdateSummary(string cryptoCode)
        {
            if (!DaemonRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var daemonRpcClient) ||
                !WalletRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var walletRpcClient))
            {
                return null;
            }

            var summary = new MoneroLikeSummary();
            try
            {
                var daemonResult =
                    await daemonRpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, GetInfoResponse>("get_info",
                        JsonRpcClient.NoRequestModel.Instance);
                summary.TargetHeight = daemonResult.TargetHeight.GetValueOrDefault(0);
                summary.CurrentHeight = daemonResult.Height;
                summary.TargetHeight = summary.TargetHeight == 0 ? summary.CurrentHeight : summary.TargetHeight;
                summary.Synced = !daemonResult.BusySyncing;
                summary.UpdatedAt = DateTime.UtcNow;
                summary.DaemonAvailable = true;
            }
            catch
            {
                summary.DaemonAvailable = false;
            }

            bool walletCreated = false;
        retry:
            try
            {
                var walletResult =
                    await walletRpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, GetHeightResponse>(
                        "get_height", JsonRpcClient.NoRequestModel.Instance);
                summary.WalletHeight = walletResult.Height;
                summary.WalletAvailable = true;
            }
            catch when (environment.CheatMode && !walletCreated)
            {
                await CreateTestWallet(walletRpcClient);
                walletCreated = true;
                goto retry;
            }
            catch
            {
                summary.WalletAvailable = false;
            }

            if (environment.CheatMode &&
                CashCowWalletRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var cashCow))
            {
                await MakeCashCowFat(cashCow, daemonRpcClient);
            }

            var changed = !_summaries.ContainsKey(cryptoCode) || IsAvailable(cryptoCode) != IsAvailable(summary);

            _summaries.AddOrReplace(cryptoCode, summary);
            if (changed)
            {
                _eventAggregator.Publish(new MoneroDaemonStateChange() { Summary = summary, CryptoCode = cryptoCode });
            }

            return summary;
        }

        private async Task MakeCashCowFat(JsonRpcClient cashcow, JsonRpcClient deamon)
        {
            try
            {
                var walletResult =
                    await cashcow.SendCommandAsync<JsonRpcClient.NoRequestModel, GetHeightResponse>(
                        "get_height", JsonRpcClient.NoRequestModel.Instance);
            }
            catch
            {
                _logger.LogInformation("Creating XMR cashcow wallet...");
                await CreateTestWallet(cashcow);
            }

            var balance =
                (await cashcow.SendCommandAsync<JsonRpcClient.NoRequestModel, GetBalanceResponse>("get_balance",
                    JsonRpcClient.NoRequestModel.Instance));
            if (balance.UnlockedBalance != 0)
            {
                return;
            }
            _logger.LogInformation("Mining blocks for the cashcow...");
            var address = (await cashcow.SendCommandAsync<GetAddressRequest, GetAddressResponse>("get_address", new()
            {
                AccountIndex = 0
            })).Address;
            await deamon.SendCommandAsync<GenerateBlocks, JsonRpcClient.NoRequestModel>("generateblocks", new GenerateBlocks()
            {
                WalletAddress = address,
                AmountOfBlocks = 100
            });
            _logger.LogInformation("Mining succeed!");
        }

        private static async Task CreateTestWallet(JsonRpcClient walletRpcClient)
        {
            try
            {
                await walletRpcClient.SendCommandAsync<OpenWalletRequest, JsonRpcClient.NoRequestModel>(
                    "open_wallet",
                    new OpenWalletRequest()
                    {
                        Filename = "wallet",
                        Password = "password"
                    });
                return;
            }
            catch
            {
                // ignored
            }

            await walletRpcClient.SendCommandAsync<CreateWalletRequest, JsonRpcClient.NoRequestModel>("create_wallet",
                new()
                {
                    Filename = "wallet",
                    Password = "password",
                    Language = "English"
                });
        }


        public class MoneroDaemonStateChange
        {
            public string CryptoCode { get; set; }
            public MoneroLikeSummary Summary { get; set; }
        }

        public class MoneroLikeSummary
        {
            public bool Synced { get; set; }
            public long CurrentHeight { get; set; }
            public long WalletHeight { get; set; }
            public long TargetHeight { get; set; }
            public DateTime UpdatedAt { get; set; }
            public bool DaemonAvailable { get; set; }
            public bool WalletAvailable { get; set; }
        }
    }
}