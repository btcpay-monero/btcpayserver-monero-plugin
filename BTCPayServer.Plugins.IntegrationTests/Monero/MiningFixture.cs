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
        config.SetSeed("alerts comb talent taxi beer skew mowing lukewarm gifts furnished woven boss thirsty faked jeans upon punch uttered woken typist mohawk enigma mostly noodles boss");
        await CashCowWallet.CreateWallet(config);
        await MineAtLeastToHeight(71);

        List<MoneroAccount> moneroAccounts = await CashCowWallet.GetAccounts(true, false, null);
        foreach (MoneroAccount account in moneroAccounts)
        {
            TestContext.Current.SendDiagnosticMessage(
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
                "9zUKfjbRQQyXtbmvBkvHYMZ5eZBnZP7BeUatxfyPKvQcE7L328PQM2v17e6XApTHzeFmrfypgbDSdVFGbB5xuapQEQU4ufE",
                1,
                false,
                false
            );
            TestContext.Current.SendDiagnosticMessage("Mining started.");
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
                TestContext.Current.SendDiagnosticMessage($"Current height: {height}/{targetHeight}");
            }

            await Task.Delay(1000);
        }

        TestContext.Current.SendDiagnosticMessage($"Mining to height {targetHeight} completed.");
    }
}