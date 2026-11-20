using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Plugins.Monero.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Monero.Services;

public class MoneroLwsLoadUpService(
    ILogger<MoneroLwsLoadUpService> logger,
    IMoneroLwsProvider lws,
    IServiceScopeFactory scopeFactory) : IHostedService
{
    private const string CryptoCode = "XMR";

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Loading existing wallets from DB into LWS");

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MoneroDbContext>();

            var wallet = await db.StoreWallets
                .AsNoTracking()
                .SingleOrDefaultAsync(w => w.StoreId == MoneroStoreWallet.DefaultKey, ct);

            if (wallet is not null)
            {
                await lws.Login(wallet.PrimaryAddress, wallet.PrivateViewKey);
                await lws.UpdateSummary(CryptoCode, wallet.PrimaryAddress, wallet.PrivateViewKey);
            }
            else
            {
                logger.LogInformation("No default {CryptoCode} wallet stored, skipping LWS login.", CryptoCode);
            }

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reach LWS for {CryptoCode}.", CryptoCode);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}