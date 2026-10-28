using BTCPayServer.Abstractions.Contracts;

using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Monero.Data;

[PluginDatabase("Monero_Migrations")]
public class MoneroDbContext(DbContextOptions<MoneroDbContext> options) : BasePluginDbContext<MoneroDbContext>(options)
{
    public MoneroDbContext() : this(new DbContextOptions<MoneroDbContext>())
    {
    }

    public DbSet<MoneroStoreWallet> StoreWallets { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("MoneroPlugin");
    }
}