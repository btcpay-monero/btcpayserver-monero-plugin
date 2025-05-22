using BTCPayServer.Plugins.Monero.RPC;
using Xunit;

namespace BTCPayServer.Plugins.Monero.Tests.RPC
{
    public class MoneroEventTest
    {
        [Fact]
        public void DefaultInitialization_ShouldHaveNullProperties()
        {
            var moneroEvent = new MoneroEvent();

            Assert.Null(moneroEvent.BlockHash);
            Assert.Null(moneroEvent.TransactionHash);
            Assert.Null(moneroEvent.CryptoCode);
        }

        [Fact]
        public void PropertyAssignment_ShouldSetAndRetrieveValues()
        {
            var moneroEvent = new MoneroEvent
            {
                BlockHash = "block123",
                TransactionHash = "tx456",
                CryptoCode = "XMR"
            };

            Assert.Equal("block123", moneroEvent.BlockHash);
            Assert.Equal("tx456", moneroEvent.TransactionHash);
            Assert.Equal("XMR", moneroEvent.CryptoCode);
        }

        [Theory]
        [InlineData("block123", "tx456", "XMR", "XMR: Tx Update New Block (tx456block123)")]
        public void ToString_ShouldReturnCorrectString(string blockHash, string transactionHash, string cryptoCode, string expected)
        {
            var moneroEvent = new MoneroEvent
            {
                BlockHash = blockHash,
                TransactionHash = transactionHash,
                CryptoCode = cryptoCode
            };

            var result = moneroEvent.ToString();

            Assert.Equal(expected, result);
        }

        
    }
}