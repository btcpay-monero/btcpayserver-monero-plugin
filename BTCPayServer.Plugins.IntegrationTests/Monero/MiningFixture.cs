using Monero.Daemon;
using Monero.Wallet;
using Monero.Wallet.Common;

using Xunit;

namespace BTCPayServer.Plugins.IntegrationTests.Monero;

[CollectionDefinition("Mining")]
public class MoneroMiningCollection : ICollectionFixture<MiningFixture>;

public class MiningFixture : IAsyncLifetime
{
    private static readonly MoneroDaemonRpc DaemonRpc = MoneroIntegrationTestBase.GetDaemonRpc();
    private static readonly MoneroWalletRpc CashCowWallet = MoneroIntegrationTestBase.GetCashCowWalletRpc();

    public async ValueTask InitializeAsync()
    {
        MoneroWalletConfig config = new();
        config.SetPrimaryAddress(
            "9yEzCbcYdg6MqZ5AkEh8V3YCriyN1tvmtWEHdBEUHkF6D6kN1MMD2Kd2QVWoTY67aNHNYKMUP3xfteLS2QNavJxpJdx6mWj");
        config.SetPrivateViewKey("1f4668e8c1979b4c7dae13dc149fd95cd7ff2883becffe160c21f9e02c821c08");
        await CashCowWallet.CreateWallet(config);
        await MineAtLeastToHeight(71);

        List<MoneroAccount> moneroAccounts = await CashCowWallet.GetAccounts(true, false, null);
        foreach (MoneroAccount account in moneroAccounts)
        {
            Console.WriteLine(
                $"Wallet's account with index {account.AccountIndex}: total balance {account.Balance}, unlocked balance {account.UnlockedBalance}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DaemonRpc.StopMining();
    }

    private static async Task MineAtLeastToHeight(ulong targetHeight)
    {
        var currentHeight = await CashCowWallet.GetHeight();
        if (currentHeight >= targetHeight) { return; }

        var miningStatus = await DaemonRpc.GetMiningStatus();
        if (miningStatus.IsActive != true)
        {
            await DaemonRpc.StartMining(
                "9yEzCbcYdg6MqZ5AkEh8V3YCriyN1tvmtWEHdBEUHkF6D6kN1MMD2Kd2QVWoTY67aNHNYKMUP3xfteLS2QNavJxpJdx6mWj",
                1,
                false,
                false
            );
            Console.WriteLine("Mining started.");
        }

        ulong lastLoggedHeight = currentHeight;
        while (true)
        {
            var height = await DaemonRpc.GetHeight();
            if (height >= targetHeight)
            {
                break;
            }

            if (height != lastLoggedHeight)
            {
                lastLoggedHeight = height;
                Console.WriteLine($"Current height: {height}/{targetHeight}");
            }

            await Task.Delay(1000);
        }

        Console.WriteLine($"Mining to height {targetHeight} completed.");
    }
}