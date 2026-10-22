using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Monero.Services;

public class MoneroRpcLoadUpService : IHostedService
{
    private const string CryptoCode = "XMR";
    private readonly ILogger<MoneroRpcLoadUpService> _logger;
    private readonly IMoneroRpcProvider _moneroRpcProvider;

    public MoneroRpcLoadUpService(ILogger<MoneroRpcLoadUpService> logger, IMoneroRpcProvider moneroRpcProvider)
    {
        _moneroRpcProvider = moneroRpcProvider;
        _logger = logger;
    }

    [Obsolete("Remove optional password parameter")]
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Attempt to load existing wallet with RPC");

            string walletDir = _moneroRpcProvider.GetWalletDirectory(CryptoCode);
            if (!string.IsNullOrEmpty(walletDir))
            {
                string password = "";
                string passwordFile = Path.Combine(walletDir, "password");
                bool passwordFileExists = File.Exists(passwordFile);
                if (passwordFileExists)
                {
                    password = await File.ReadAllTextAsync(passwordFile, cancellationToken);
                    password = password.Trim();
                }

                await _moneroRpcProvider.OpenWallet(CryptoCode, "wallet", password);
                await _moneroRpcProvider.UpdateSummary(CryptoCode);

                if (passwordFileExists)
                {
                    _logger.LogInformation("Old wallet file password detected - deprecation started");
                    await _moneroRpcProvider.ChangeWalletPassword(CryptoCode, password, "");
                    _logger.LogInformation("Wallet file password cleared");
                    File.Delete(passwordFile);
                    _logger.LogInformation("Legacy wallet password file deleted - deprecation finished");
                }

                _logger.LogInformation("Wallet successfully loaded");
            }
            else
            {
                _logger.LogInformation("No wallet directory configured, skipping wallet migration");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to load {CryptoCode} wallet. Error Message: {ErrorMessage}", CryptoCode,
                ex.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}