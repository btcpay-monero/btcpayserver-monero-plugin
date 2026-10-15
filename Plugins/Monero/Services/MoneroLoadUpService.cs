using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Monero.Data;
using BTCPayServer.Plugins.Monero.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Monero.Services;

public class MoneroLoadUpService(
    ILogger<MoneroLoadUpService> logger,
    IMoneroRpcProvider moneroRpcProvider,
    IDbContextFactory<MoneroDbContext> moneroDbFactory,
    StoreRepository storeRepository,
    PaymentMethodHandlerDictionary handlers)
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
            await using var moneroDb = await moneroDbFactory.CreateDbContextAsync(ct);
            if (await moneroDb.StoreWallets.AnyAsync(ct))
            {
                return; // already recorded
            }

            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(CryptoCode);
            var stores = await storeRepository.GetStores();
            var activeStoreIds = stores
                .Where(s => s.GetPaymentMethodConfigs(handlers).Any(c => c.Value is MoneroPaymentPromptDetails)
                            && !s.GetStoreBlob().GetExcludedPaymentMethods().Match(pmi))
                .Select(s => s.Id)
                .OrderBy(id => id)
                .ToList();

            if (activeStoreIds.Count == 0)
            {
                return;
            }

            if (activeStoreIds.Count > 1)
            {
                logger.LogWarning(
                    "Found {Count} active XMR stores, recording the wallet for {StoreId} (first by ID)",
                    activeStoreIds.Count, activeStoreIds[0]);
            }

            moneroDb.StoreWallets.Add(new MoneroStoreWallet
            {
                StoreId = activeStoreIds[0],
                PrimaryAddress = await moneroRpcProvider.GetPrimaryAddress(CryptoCode),
                PrivateViewKey = await moneroRpcProvider.GetPrivateViewKey(CryptoCode)
            });
            await moneroDb.SaveChangesAsync(ct);
            logger.LogInformation("Recorded {CryptoCode} primary address and view key for store {StoreId}",
                CryptoCode, activeStoreIds[0]);
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