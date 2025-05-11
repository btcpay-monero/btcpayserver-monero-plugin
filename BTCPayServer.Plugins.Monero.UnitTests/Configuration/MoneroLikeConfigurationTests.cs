using BTCPayServer.Plugins.Monero.Configuration;
using Xunit;

namespace BTCPayServer.Plugins.Monero.UnitTests.Configuration
{
    public class MoneroLikeConfigurationTests
    {
        [Trait("Category", "Unit")]
        [Fact]
        public void MoneroLikeConfiguration_ShouldInitializeWithEmptyDictionary()
        {
            var config = new MoneroLikeConfiguration();

            Assert.NotNull(config.MoneroLikeConfigurationItems);
            Assert.Empty(config.MoneroLikeConfigurationItems);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void MoneroLikeConfigurationItem_ShouldSetAndGetProperties()
        {
            var configItem = new MoneroLikeConfigurationItem
            {
                DaemonRpcUri = new Uri("http://localhost:18081"),
                InternalWalletRpcUri = new Uri("http://localhost:18082"),
                WalletDirectory = "/wallets",
                Username = "user",
                Password = "password",
                CashCowWalletRpcUri = new Uri("http://localhost:18083")
            };

            Assert.Equal("http://localhost:18081/", configItem.DaemonRpcUri.ToString());
            Assert.Equal("http://localhost:18082/", configItem.InternalWalletRpcUri.ToString());
            Assert.Equal("/wallets", configItem.WalletDirectory);
            Assert.Equal("user", configItem.Username);
            Assert.Equal("password", configItem.Password);
            Assert.Equal("http://localhost:18083/", configItem.CashCowWalletRpcUri.ToString());
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void MoneroLikeConfiguration_ShouldAddAndRetrieveItems()
        {
            var config = new MoneroLikeConfiguration();
            var configItem = new MoneroLikeConfigurationItem
            {
                DaemonRpcUri = new Uri("http://localhost:18081"),
                InternalWalletRpcUri = new Uri("http://localhost:18082"),
                WalletDirectory = "/wallets",
                Username = "user",
                Password = "password"
            };

            config.MoneroLikeConfigurationItems.Add("XMR", configItem);

            Assert.Single(config.MoneroLikeConfigurationItems);
            Assert.True(config.MoneroLikeConfigurationItems.ContainsKey("XMR"));
            Assert.Equal(configItem, config.MoneroLikeConfigurationItems["XMR"]);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void MoneroLikeConfiguration_ShouldHandleDuplicateKeys()
        {
            var config = new MoneroLikeConfiguration();
            var configItem1 = new MoneroLikeConfigurationItem
            {
                DaemonRpcUri = new Uri("http://localhost:18081")
            };
            var configItem2 = new MoneroLikeConfigurationItem
            {
                DaemonRpcUri = new Uri("http://localhost:18082")
            };

            config.MoneroLikeConfigurationItems.Add("XMR", configItem1);

            Assert.Throws<ArgumentException>(() =>
                config.MoneroLikeConfigurationItems.Add("XMR", configItem2));
        }
    }
}