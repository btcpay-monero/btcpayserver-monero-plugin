using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Monero.Data;

public static class MoneroDbContextFactoryExtensions
{
    extension(IDbContextFactory<MoneroDbContext> factory)
    {
        public Task SaveDefaultWalletAsync(string primaryAddress,
            string privateViewKey) =>
            factory.SaveDefaultWalletAsync(primaryAddress, privateViewKey, CancellationToken.None);

        public async Task SaveDefaultWalletAsync(string primaryAddress,
            string privateViewKey,
            CancellationToken ct)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var wallet = await db.StoreWallets.FindAsync([MoneroStoreWallet.DefaultKey], ct);
            if (wallet is null)
            {
                db.StoreWallets.Add(new MoneroStoreWallet
                {
                    StoreId = MoneroStoreWallet.DefaultKey,
                    PrimaryAddress = primaryAddress,
                    PrivateViewKey = privateViewKey
                });
            }
            else
            {
                wallet.PrimaryAddress = primaryAddress;
                wallet.PrivateViewKey = privateViewKey;
            }

            await db.SaveChangesAsync(ct);
        }
    }
}