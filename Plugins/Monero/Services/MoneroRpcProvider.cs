using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net.Http;
using System.Threading.Tasks;

using BTCPayServer.Plugins.Monero.Configuration;

using Monero.Common;
using Monero.Daemon.Common;
using Monero.Wallet.Rpc;

using NBitcoin;

// using NBitcoin;

namespace BTCPayServer.Plugins.Monero.Services
{
    public class MoneroRpcProvider
    {
        private readonly MoneroLikeConfiguration _moneroLikeConfiguration;
        private readonly EventAggregator _eventAggregator;
        public ImmutableDictionary<string, MoneroRpcConnection> DaemonRpcClients;
        public ImmutableDictionary<string, MoneroRpcConnection> WalletRpcClients;

        public ConcurrentDictionary<string, MoneroLikeSummary> Summaries { get; } = new();

        public MoneroRpcProvider(MoneroLikeConfiguration moneroLikeConfiguration,
            EventAggregator eventAggregator,
            IHttpClientFactory httpClientFactory)
        {
            _moneroLikeConfiguration = moneroLikeConfiguration;
            _eventAggregator = eventAggregator;
            DaemonRpcClients =
                _moneroLikeConfiguration.MoneroLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new MoneroRpcConnection(pair.Value.DaemonRpcUri, pair.Value.Username, pair.Value.Password,
                        httpClientFactory.CreateClient($"{pair.Key}client")));
            WalletRpcClients =
                _moneroLikeConfiguration.MoneroLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new MoneroRpcConnection(pair.Value.InternalWalletRpcUri, "", "",
                        httpClientFactory.CreateClient($"{pair.Key}client")));
        }

        public bool IsConfigured(string cryptoCode) => WalletRpcClients.ContainsKey(cryptoCode) && DaemonRpcClients.ContainsKey(cryptoCode);
        public bool IsAvailable(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            return Summaries.ContainsKey(cryptoCode) && IsAvailable(Summaries[cryptoCode]);
        }

        private bool IsAvailable(MoneroLikeSummary summary)
        {
            return summary.Synced &&
                   summary.WalletAvailable;
        }

        public async Task CloseWallet(string cryptoCode)
        {
            if (!WalletRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var walletRpcClient))
            {
                throw new InvalidOperationException($"Wallet RPC client not found for {cryptoCode}");
            }

            await walletRpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, object>(
                "close_wallet", JsonRpcClient.NoRequestModel.Instance);
        }

        public string GetWalletDirectory(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            return !_moneroLikeConfiguration.MoneroLikeConfigurationItems.TryGetValue(cryptoCode, out var configItem)
                ? null
                : configItem.WalletDirectory;
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
                    await daemonRpcClient.SendCommandAsync<NoRequestModel, MoneroDaemonInfo>("get_info",
                        NoRequestModel.Instance);
                summary.TargetHeight = daemonResult.TargetHeight.GetValueOrDefault(0);
                summary.CurrentHeight = daemonResult.Height;
                summary.DaemonVersion = daemonResult.Version;
                summary.Restricted = daemonResult.Restricted;
                summary.TargetHeight = summary.TargetHeight == 0 ? summary.CurrentHeight : summary.TargetHeight;
                summary.Synced = !daemonResult.BusySyncing;
                summary.UpdatedAt = DateTime.UtcNow;
                summary.DaemonAvailable = true;
            }
            catch
            {
                summary.DaemonAvailable = false;
            }
            try
            {
                var walletResult =
                    await walletRpcClient.SendCommandAsync<NoRequestModel, GetHeightResponse>(
                        "get_height", NoRequestModel.Instance);
                summary.WalletHeight = walletResult.Height;
                summary.WalletAvailable = true;
            }
            catch
            {
                summary.WalletAvailable = false;
            }

            var changed = !Summaries.ContainsKey(cryptoCode) || IsAvailable(cryptoCode) != IsAvailable(summary);

            Summaries.AddOrReplace(cryptoCode, summary);
            if (changed)
            {
                _eventAggregator.Publish(new MoneroDaemonStateChange() { Summary = summary, CryptoCode = cryptoCode });
            }

            return summary;
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
            public ulong WalletHeight { get; set; }
            public long TargetHeight { get; set; }
            public DateTime UpdatedAt { get; set; }
            public bool DaemonAvailable { get; set; }
            public string DaemonVersion { get; set; }
            public bool Restricted { get; set; }
            public bool WalletAvailable { get; set; }
        }
    }
}