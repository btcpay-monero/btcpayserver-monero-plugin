using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Plugins.Monero.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Monero.Services;

public class MoneroLoadUpService(
    ILogger<MoneroLoadUpService> logger,
    IMoneroRpcProvider moneroRpcProvider,
    IDbContextFactory<MoneroDbContext> moneroDbFactory)
    : IHostedService
{
    private const string CryptoCode = "XMR";

    [Obsolete("Remove optional password parameter")]
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Attempt to load existing wallet");

            string walletDir = moneroRpcProvider.GetWalletDirectory(CryptoCode);
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

                await moneroRpcProvider.OpenWallet(CryptoCode, "wallet", password);
                await moneroRpcProvider.UpdateSummary(CryptoCode);

                if (passwordFileExists)
                {
                    logger.LogInformation("Old wallet file password detected - deprecation started");
                    await moneroRpcProvider.ChangeWalletPassword(CryptoCode, password, "");
                    logger.LogInformation("Wallet file password cleared");
                    File.Delete(passwordFile);
                    logger.LogInformation("Legacy wallet password file deleted - deprecation finished");
                }

                logger.LogInformation("Wallet successfully loaded");

                await RecordPrimaryAddressAndViewKeyAsync(cancellationToken);
            }
            else
            {
                logger.LogInformation("No wallet directory configured, skipping wallet migration");
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to load {CryptoCode} wallet. Error Message: {ErrorMessage}", CryptoCode,
                ex.Message);
        }
    }

    private async Task RecordPrimaryAddressAndViewKeyAsync(CancellationToken ct)
    {
        try
        {
            var address = await moneroRpcProvider.GetPrimaryAddress(CryptoCode);
            var viewKey = await moneroRpcProvider.GetPrivateViewKey(CryptoCode);
            await moneroDbFactory.SaveDefaultWalletAsync(address, viewKey, ct);
            logger.LogInformation("Recorded {CryptoCode} primary address and view key", CryptoCode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record {CryptoCode} primary address and view key", CryptoCode);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}