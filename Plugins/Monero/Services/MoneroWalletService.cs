using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Plugins.Monero.Configuration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Monero.Services
{
    public class MoneroWalletService : IHostedService
    {
        private const string CryptoCode = "XMR";
        private readonly MoneroRpcProvider _rpcProvider;
        private readonly ILogger<MoneroWalletService> _logger;
        private readonly ISettingsRepository _settingsRepository;
        private MoneroWalletState _walletState;

        public MoneroWalletService(
            MoneroRpcProvider rpcProvider,
            ILogger<MoneroWalletService> logger,
            ISettingsRepository settingsRepository)
        {
            _rpcProvider = rpcProvider;
            _logger = logger;
            _settingsRepository = settingsRepository;
            _walletState = new MoneroWalletState();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!_rpcProvider.IsConfigured(CryptoCode))
                {
                    _logger.LogWarning($"{CryptoCode} RPC not configured");
                    return;
                }

                var savedState = await _settingsRepository.GetSettingAsync<MoneroWalletState>();

                if (savedState?.PasswordFileMigration != true)
                {
                    var migrated = await TryMigratePasswordFile();
                    if (migrated != null)
                    {
                        savedState = migrated;
                    }
                    else if (savedState == null)
                    {
                        savedState = new MoneroWalletState();
                    }
                }

                _walletState = savedState;

                if (!_walletState.IsInitialized)
                {
                    _logger.LogInformation("No wallet configured - user will set up via UI");
                    return;
                }

                string walletName = _walletState.ActiveWalletName;

                if (string.IsNullOrEmpty(walletName))
                {
                    _logger.LogWarning("Active wallet address set but wallet record not found");
                    return;
                }

                var result = await _rpcProvider.OpenWallet(CryptoCode, walletName, "");

                if (result)
                {
                    _walletState.IsConnected = true;
                    _logger.LogInformation($"Successfully opened wallet {walletName} on startup");
                }
                else
                {
                    _logger.LogWarning($"Failed to open wallet {walletName} on startup");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during {CryptoCode} wallet startup");
            }
        }

        private async Task<MoneroWalletState> TryMigratePasswordFile()
        {
            try
            {
                string walletDir = _rpcProvider.GetWalletDirectory(CryptoCode);
                string passwordFile = Path.Combine(walletDir, "password");
                const string walletName = "wallet";

                if (string.IsNullOrEmpty(walletDir) || !File.Exists(passwordFile))
                {
                    _logger.LogWarning("Wallet directory not set or password file missing during migration.");
                    return null;
                }

                string[] availableWallets = _rpcProvider.GetWalletList(CryptoCode);
                if (availableWallets is null || !availableWallets.Contains(walletName))
                {
                    _logger.LogWarning("Password file found but no file named wallet.keys found. Move wallet.keys back to the wallet directory or delete the password file to complete the migration. ");
                    return null;
                }

                _logger.LogInformation("Found password file, migrating wallet to remove password requirement");

                string password = (await File.ReadAllTextAsync(passwordFile)).Trim();
                bool passwordRemoved = await _rpcProvider.OpenWallet(CryptoCode, walletName, "");

                if (!passwordRemoved)
                {
                    bool opened = await _rpcProvider.OpenWallet(CryptoCode, walletName, password);
                    if (!opened)
                    {
                        _logger.LogWarning($"Failed to open wallet {walletName} during migration");
                        return null;
                    }

                    bool passwordChanged = await _rpcProvider.ChangeWalletPassword(CryptoCode, password, "");
                    if (!passwordChanged)
                    {
                        _logger.LogError("Failed to remove wallet password during migration");
                        await _rpcProvider.CloseWallet(CryptoCode);
                        return null;
                    }

                    bool stored = await _rpcProvider.StoreWallet(CryptoCode);
                    if (!stored)
                    {
                        _logger.LogError("Failed to store wallet after removing password");
                        await _rpcProvider.CloseWallet(CryptoCode);
                        return null;
                    }
                }

                var primaryAddressResponse = await _rpcProvider.GetAddress(CryptoCode, 0, 0);
                string primaryAddress = primaryAddressResponse?.Address;

                if (string.IsNullOrEmpty(primaryAddress))
                {
                    _logger.LogError("Failed to get primary address during migration");
                    await _rpcProvider.CloseWallet(CryptoCode);
                    return null;
                }

                var walletState = new MoneroWalletState
                {
                    ActiveWalletAddress = primaryAddress,
                    LastActivatedAt = DateTimeOffset.UtcNow,
                    IsConnected = true,
                    PasswordFileMigration = true,
                    Wallets = new Dictionary<string, string> { [primaryAddress] = walletName }
                };

                await _settingsRepository.UpdateSetting(walletState);
                _logger.LogInformation($"Successfully migrated wallet {walletName} to remove password requirement.");

                return walletState;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during wallet migration");
                return null;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_walletState.IsConnected)
                {
                    await _rpcProvider.CloseWallet(CryptoCode);
                    _walletState.IsConnected = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error closing wallet during shutdown");
            }
        }

        public async Task<(bool Success, string ErrorMessage)> SetActiveWallet(string primaryAddress, string changedByStoreId)
        {
            try
            {
                if (!_walletState.Wallets.TryGetValue(primaryAddress, out var walletName))
                {
                    return (false, $"Wallet with address {primaryAddress} is not imported.");
                }

                if (_walletState.IsConnected)
                {
                    await _rpcProvider.CloseWallet(CryptoCode);
                    _walletState.IsConnected = false;
                }

                var opened = await _rpcProvider.OpenWallet(CryptoCode, walletName, "");
                if (!opened)
                {
                    return (false, "Failed to open wallet.");
                }

                await StoreWalletState(primaryAddress, changedByStoreId);
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error setting active wallet to address {primaryAddress}");
                return (false, ex.Message);
            }
        }

        public MoneroWalletState GetWalletState()
        {
            return _walletState;
        }

        private async Task StoreWalletState(string primaryAddress, string storeId)
        {
            string walletName = _walletState.Wallets[primaryAddress];

            _walletState.ActiveWalletAddress = primaryAddress;
            _walletState.LastActivatedAt = DateTimeOffset.UtcNow;
            _walletState.IsConnected = true;

            await _settingsRepository.UpdateSetting(_walletState);
            await _rpcProvider.UpdateSummary(CryptoCode);
            _logger.LogInformation($"Active wallet changed to {walletName} by store {storeId}");
        }

        public bool HasDeprecatedPasswordFile()
        {
            string walletDir = _rpcProvider.GetWalletDirectory(CryptoCode);
            if (string.IsNullOrEmpty(walletDir))
            {
                return false;
            }

            return File.Exists(Path.Combine(walletDir, "password"));
        }

        public async Task<(bool Success, string ErrorMessage)> CreateAndActivateWallet(
            string walletName,
            string primaryAddress,
            string privateViewKey,
            int restoreHeight,
            string createdByStoreId)
        {
            try
            {
                _logger.LogInformation($"Creating and activating wallet {walletName} for store {createdByStoreId}");

                var (createSuccess, createError) = await _rpcProvider.CreateWalletFromKeys(
                    CryptoCode,
                    walletName,
                    primaryAddress,
                    privateViewKey,
                    "",
                    restoreHeight);

                if (!createSuccess)
                {
                    _logger.LogError($"Failed to create wallet {walletName}: {createError}");
                    return (false, createError);
                }

                _logger.LogInformation($"Successfully created wallet {walletName}");

                _walletState.Wallets[primaryAddress] = walletName;
                await StoreWalletState(primaryAddress, createdByStoreId);

                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating and activating wallet {walletName}");
                return (false, ex.Message);
            }
        }

        public async Task<(bool Success, string ErrorMessage)> DeleteWallet(string primaryAddress)
        {
            try
            {
                if (!_walletState.Wallets.TryGetValue(primaryAddress, out var walletName))
                {
                    return (false, $"Wallet with address {primaryAddress} is not imported.");
                }
                _logger.LogInformation($"Deleting wallet {walletName} (primary address: {primaryAddress})");

                bool isActiveWallet = primaryAddress == _walletState.ActiveWalletAddress;
                if (isActiveWallet)
                {
                    if (_walletState.IsConnected)
                    {
                        await _rpcProvider.CloseWallet(CryptoCode);
                    }
                    _walletState.IsConnected = false;
                    _walletState.ActiveWalletAddress = null;
                }

                _walletState.Wallets.Remove(primaryAddress);
                await _settingsRepository.UpdateSetting(_walletState);

                var deleted = _rpcProvider.DeleteWallet(CryptoCode, walletName);
                if (!deleted)
                {
                    _logger.LogWarning($"Failed to delete wallet files for {walletName}, file may still be on disk. Remove if still present.");
                }

                _logger.LogInformation($"Successfully deleted wallet {walletName}");
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting wallet with address {primaryAddress}");
                return (false, ex.Message);
            }
        }
    }
}