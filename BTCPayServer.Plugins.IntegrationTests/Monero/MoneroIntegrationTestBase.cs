using BTCPayServer.Tests;

using Monero.Common;
using Monero.Daemon;
using Monero.Wallet;

using Xunit;

namespace BTCPayServer.Plugins.IntegrationTests.Monero;

public class MoneroIntegrationTestBase : UnitTestBase, IAsyncLifetime
{
    protected MoneroIntegrationTestBase(ITestOutputHelper helper) : base(helper)
    {
        SetDefaultEnv("BTCPAY_XMR_DAEMON_URI", "http://127.0.0.1:18081");
        SetDefaultEnv("BTCPAY_XMR_WALLET_DAEMON_URI", "http://127.0.0.1:18082");
        SetDefaultEnv("BTCPAY_XMR_WALLET_DAEMON_WALLETDIR", "/wallet");
        SetDefaultEnv("BTCPAY_NODEFAULTCHAIN", "true");
        SetDefaultEnv("TESTS_PORT", "14142");
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => IntegrationTestUtils.CleanUpAsync();

    public static MoneroDaemonRpc GetDaemonRpc()
    {
        return new MoneroDaemonRpc(new MoneroRpcConnection(new Uri(Environment.GetEnvironmentVariable("BTCPAY_XMR_DAEMON_URI") ?? "http://127.0.0.1:18081"), "", ""));
    }

    public static MoneroWalletRpc GetCashCowWalletRpc()
    {
        return new MoneroWalletRpc(new MoneroRpcConnection(new Uri(Environment.GetEnvironmentVariable("BTCPAY_XMR_CASHCOW_WALLET_DAEMON_URI") ?? "http://127.0.0.1:18092"), "", ""));
    }

    private static void SetDefaultEnv(string key, string defaultValue)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
        {
            Environment.SetEnvironmentVariable(key, defaultValue);
        }
    }
}