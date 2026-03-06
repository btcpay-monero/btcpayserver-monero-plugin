using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using BTCPayServer.Plugins.Monero.Configuration;
using BTCPayServer.Plugins.Monero.RPC;
using BTCPayServer.Plugins.Monero.RPC.Models;

using Microsoft.Extensions.Logging;

using NBitcoin;

namespace BTCPayServer.Plugins.Monero.Services
{
    public class MoneroRpcProvider
    {
        private readonly MoneroLikeConfiguration _moneroLikeConfiguration;
        private readonly EventAggregator _eventAggregator;
        public ImmutableDictionary<string, JsonRpcClient> DaemonRpcClients;
        public ImmutableDictionary<string, JsonRpcClient> WalletRpcClients;
        private readonly ILogger<MoneroRpcProvider> _logger;

        public ConcurrentDictionary<string, MoneroLikeSummary> Summaries { get; } = new();

        public MoneroRpcProvider(MoneroLikeConfiguration moneroLikeConfiguration,
            EventAggregator eventAggregator,
            ILogger<MoneroRpcProvider> logger,
            IHttpClientFactory httpClientFactory)
        {
            _moneroLikeConfiguration = moneroLikeConfiguration;
            _eventAggregator = eventAggregator;
            _logger = logger;
            DaemonRpcClients =
                _moneroLikeConfiguration.MoneroLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new JsonRpcClient(pair.Value.DaemonRpcUri, pair.Value.Username, pair.Value.Password,
                        httpClientFactory.CreateClient($"{pair.Key}client")));
            WalletRpcClients =
                _moneroLikeConfiguration.MoneroLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new JsonRpcClient(pair.Value.InternalWalletRpcUri, "", "",
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
        public async Task<bool> OpenWallet(string cryptoCode, string filename, string password)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!WalletRpcClients.TryGetValue(cryptoCode, out var walletRpcClient))
            {
                return false;
            }

            try
            {
                await walletRpcClient.SendCommandAsync<OpenWalletRequest, object>(
                    "open_wallet", new OpenWalletRequest
                    {
                        Filename = filename,
                        Password = password
                    });

                await UpdateSummary(cryptoCode);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to open wallet {filename}");
                return false;
            }
        }
        public async Task<bool> CloseWallet(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!WalletRpcClients.TryGetValue(cryptoCode, out var walletRpcClient))
            {
                return false;
            }
            try
            {
                await walletRpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, object>(
                    "close_wallet", JsonRpcClient.NoRequestModel.Instance);

                if (Summaries.TryGetValue(cryptoCode, out var summary))
                {
                    summary.WalletAvailable = false;
                    Summaries.AddOrReplace(cryptoCode, summary);
                    _eventAggregator.Publish(new MoneroDaemonStateChange() { Summary = summary, CryptoCode = cryptoCode });
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to close wallet");
                return false;
            }
        }

        public async Task<bool> ChangeWalletPassword(string cryptoCode, string oldPassword, string newPassword)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!WalletRpcClients.TryGetValue(cryptoCode, out var walletRpcClient))
            {
                return false;
            }

            try
            {
                await walletRpcClient.SendCommandAsync<ChangeWalletPasswordRequest, object>(
                    "change_wallet_password", new ChangeWalletPasswordRequest
                    {
                        OldPassword = oldPassword,
                        NewPassword = newPassword
                    });
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to change wallet password");
                return false;
            }
        }

        public async Task<bool> StoreWallet(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!WalletRpcClients.TryGetValue(cryptoCode, out var walletRpcClient))
            {
                return false;
            }
            try
            {
                await walletRpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, object>("store", JsonRpcClient.NoRequestModel.Instance);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store wallet");
                return false;
            }
        }

        public async Task<(bool Success, string ErrorMessage)> CreateWalletFromKeys(
            string cryptoCode,
            string walletName,
            string primaryAddress,
            string privateViewKey,
            string password,
            int restoreHeight)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!WalletRpcClients.TryGetValue(cryptoCode, out var walletRpcClient))
            {
                return (false, $"Wallet RPC client not configured for {cryptoCode}");
            }

            if (!IsValidWalletName(walletName))
            {
                return (false, "Invalid wallet name. Only alphanumeric characters, dashes, and underscores are allowed (max 64 characters).");
            }

            try
            {
                GenerateFromKeysResponse response = await walletRpcClient.SendCommandAsync<GenerateFromKeysRequest, GenerateFromKeysResponse>(
                    "generate_from_keys",
                    new GenerateFromKeysRequest
                    {
                        PrimaryAddress = primaryAddress,
                        PrivateViewKey = privateViewKey,
                        WalletFileName = walletName,
                        RestoreHeight = restoreHeight,
                        Password = password
                    });

                if (response?.Error != null)
                {
                    return (false, response.Error.Message);
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<GetAccountsResponse> GetAccounts(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!WalletRpcClients.TryGetValue(cryptoCode, out var walletRpcClient))
            {
                return null;
            }

            try
            {
                if (!Summaries.TryGetValue(cryptoCode, out var summary) || !summary.WalletAvailable)
                {
                    return null;
                }

                return await walletRpcClient.SendCommandAsync<GetAccountsRequest, GetAccountsResponse>("get_accounts", new GetAccountsRequest());
            }
            catch
            {
                return null;
            }
        }

        private static bool IsValidWalletName(string walletName)
        {
            return !string.IsNullOrWhiteSpace(walletName)
                && walletName.Length <= 64
                && System.Text.RegularExpressions.Regex.IsMatch(walletName, "^[a-zA-Z0-9_-]+$");
        }

        public async Task<CreateAccountResponse> CreateAccount(string cryptoCode, string label)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!WalletRpcClients.TryGetValue(cryptoCode, out var walletRpcClient))
            {
                return null;
            }

            try
            {
                return await walletRpcClient.SendCommandAsync<CreateAccountRequest, CreateAccountResponse>("create_account", new CreateAccountRequest { Label = label });
            }
            catch
            {
                return null;
            }
        }

        public async Task<GetAddressResponse> GetAddress(string cryptoCode, int accountIndex, params int[] addressIndices)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!WalletRpcClients.TryGetValue(cryptoCode, out var walletRpcClient))
            {
                return null;
            }

            try
            {
                return await walletRpcClient.SendCommandAsync<GetAddressRequest, GetAddressResponse>("get_address", new GetAddressRequest { AccountIndex = accountIndex, AddressIndex = addressIndices });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get address");
                return null;
            }
        }

        public async Task<CreateAddressResponse> CreateAddress(string cryptoCode, int accountIndex, string label = null, long? count = null)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!WalletRpcClients.TryGetValue(cryptoCode, out var walletRpcClient))
            {
                return null;
            }

            try
            {
                return await walletRpcClient.SendCommandAsync<CreateAddressRequest, CreateAddressResponse>("create_address", new CreateAddressRequest { AccountIndex = accountIndex, Label = label, Count = count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create address");
                return null;
            }
        }

        public bool DeleteWallet(string cryptoCode, string walletName)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();

            if (!_moneroLikeConfiguration.MoneroLikeConfigurationItems.TryGetValue(cryptoCode, out var configItem))
            {
                return false;
            }

            if (string.IsNullOrEmpty(configItem.WalletDirectory))
            {
                return false;
            }

            try
            {
                var walletFile = Path.Combine(configItem.WalletDirectory, walletName);
                var keysFile = walletFile + ".keys";

                if (File.Exists(walletFile))
                {
                    File.Delete(walletFile);
                }
                if (File.Exists(keysFile))
                {
                    File.Delete(keysFile);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool DeleteAllWallets()
        {
            bool complete = true;

            foreach (var configItem in _moneroLikeConfiguration.MoneroLikeConfigurationItems.Values)
            {
                if (string.IsNullOrEmpty(configItem.WalletDirectory))
                {
                    continue;
                }

                try
                {
                    if (Directory.Exists(configItem.WalletDirectory))
                    {
                        foreach (var file in Directory.GetFiles(configItem.WalletDirectory))
                        {
                            try
                            {
                                File.Delete(file);
                            }
                            catch
                            {
                                complete = false;
                            }
                        }
                    }
                }
                catch
                {
                    complete = false;
                }
            }
            return complete;
        }

        public string GetWalletDirectory(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            return !_moneroLikeConfiguration.MoneroLikeConfigurationItems.TryGetValue(cryptoCode, out var configItem)
                ? null
                : configItem.WalletDirectory;
        }

        public string[] GetWalletList(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            var walletDirectory = GetWalletDirectory(cryptoCode);

            if (string.IsNullOrEmpty(walletDirectory) || !Directory.Exists(walletDirectory))
            {
                return Array.Empty<string>();
            }

            try
            {
                return Directory.GetFiles(walletDirectory, "*.keys")
                    .Select(Path.GetFileNameWithoutExtension)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
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
                    await walletRpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, GetHeightResponse>(
                        "get_height", JsonRpcClient.NoRequestModel.Instance);
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
            public long WalletHeight { get; set; }
            public long TargetHeight { get; set; }
            public DateTime UpdatedAt { get; set; }
            public bool DaemonAvailable { get; set; }
            public string DaemonVersion { get; set; }
            public bool Restricted { get; set; }
            public bool WalletAvailable { get; set; }
        }
    }
}