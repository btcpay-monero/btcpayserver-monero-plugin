using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Monero.Services;

public class MoneroLoadUpService : IHostedService
{
    private const string CryptoCode = "XMR";
    private readonly ILogger<MoneroLoadUpService> _logger;
    private readonly MoneroRpcProvider _moneroRpcProvider;

    public MoneroLoadUpService(ILogger<MoneroLoadUpService> logger, MoneroRpcProvider moneroRpcProvider)
    {
        _moneroRpcProvider = moneroRpcProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Attempt to load existing wallet");

            string walletDir = _moneroRpcProvider.GetWalletDirectory(CryptoCode);
            string passwordFile = Path.Combine(walletDir, "password");
            if (!string.IsNullOrEmpty(walletDir))
            {
                if (File.Exists(passwordFile))
                {
                    await TryDeprecatePasswordFile(passwordFile);
                }
                await _moneroRpcProvider.OpenWallet(CryptoCode, "wallet", "");
                await _moneroRpcProvider.UpdateSummary(CryptoCode);
                _logger.LogInformation("Existing wallet successfully loaded");
            }
            else
            {
                _logger.LogInformation("No wallet configured, skipping wallet migration");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to load {CryptoCode} wallet. Error Message: {ErrorMessage}", CryptoCode,
                ex.Message);
        }
    }

    private async Task TryDeprecatePasswordFile(string passwordFile)
    {
        try
        {
            string password = (await File.ReadAllTextAsync(passwordFile));
            await _moneroRpcProvider.OpenWallet(CryptoCode, "wallet", password);
            await _moneroRpcProvider.ChangeWalletPassword(CryptoCode, password, "");
            await _moneroRpcProvider.CloseWallet(CryptoCode);
            _logger.LogInformation("Successfully migrated wallet to remove password");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during wallet password deprecation");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}