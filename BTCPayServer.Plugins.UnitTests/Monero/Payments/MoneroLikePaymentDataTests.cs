using BTCPayServer.Plugins.Monero.Payments;

using Xunit;

namespace BTCPayServer.Plugins.UnitTests.Monero.Payments
{
    public class MoneroLikePaymentDataTests
    {
        [Trait("Category", "Unit")]
        [Fact]
        public void DefaultValues_ShouldBeCorrect()
        {
            var paymentData = new MoneroLikePaymentData();

            Assert.Equal(0, paymentData.SubaddressIndex);
            Assert.Equal(0, paymentData.SubaccountIndex);
            Assert.Equal(0UL, paymentData.BlockHeight);
            Assert.Equal(0, paymentData.ConfirmationCount);
            Assert.Null(paymentData.TransactionId);
            Assert.Null(paymentData.InvoiceSettledConfirmationThreshold);
            Assert.Equal(0, paymentData.LockTime);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void Properties_ShouldBeSettable()
        {
            var paymentData = new MoneroLikePaymentData();

            paymentData.SubaddressIndex = 1;
            paymentData.SubaccountIndex = 2;
            paymentData.BlockHeight = 100;
            paymentData.ConfirmationCount = 5;
            paymentData.TransactionId = "tx123";
            paymentData.InvoiceSettledConfirmationThreshold = 10;
            paymentData.LockTime = 50;

            Assert.Equal(1, paymentData.SubaddressIndex);
            Assert.Equal(2, paymentData.SubaccountIndex);
            Assert.Equal(100UL, paymentData.BlockHeight);
            Assert.Equal(5, paymentData.ConfirmationCount);
            Assert.Equal("tx123", paymentData.TransactionId);
            Assert.Equal(10, paymentData.InvoiceSettledConfirmationThreshold);
            Assert.Equal(50, paymentData.LockTime);
        }
    }
}