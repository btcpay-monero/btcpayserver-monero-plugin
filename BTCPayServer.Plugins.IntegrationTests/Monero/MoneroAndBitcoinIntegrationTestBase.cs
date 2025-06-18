using BTCPayServer.Tests;

using Xunit.Abstractions;

namespace BTCPayServer.Plugins.IntegrationTests.Monero
{
    public class MoneroAndBitcoinIntegrationTestBase : UnitTestBase
    {

        public MoneroAndBitcoinIntegrationTestBase(ITestOutputHelper helper) : base(helper)
        {
            SetDefaultEnv("BTCPAY_XMR_DAEMON_URI", "http://127.0.0.1:18081");
            SetDefaultEnv("BTCPAY_XMR_WALLET_DAEMON_URI", "http://127.0.0.1:18082");
            SetDefaultEnv("BTCPAY_XMR_WALLET_DAEMON_WALLETDIR", "/wallet");
        }

        private static void SetDefaultEnv(string key, string defaultValue)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, defaultValue);
            }
        }
    }
}