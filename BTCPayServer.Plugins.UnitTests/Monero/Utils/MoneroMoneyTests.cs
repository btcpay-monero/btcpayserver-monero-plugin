using System.Globalization;
using Xunit;
using BTCPayServer.Plugins.Monero.Utils;

namespace BTCPayServer.Plugins.UnitTests.Monero.Utils
{
    public class MoneroMoneyTests
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData(1, "0.000000000001")]
        [InlineData(123456789012, "0.123456789012")]
        [InlineData(1000000000000, "1.000000000000")]
        public void Convert_LongToDecimal_ReturnsExpectedValue(long piconero, string expectedString)
        {
            decimal expected = decimal.Parse(expectedString, CultureInfo.InvariantCulture);
            decimal result = MoneroMoney.Convert(piconero);
            Assert.Equal(expected, result);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("0.000000000001", 1)]
        [InlineData("0.123456789012", 123456789012)]
        [InlineData("1.000000000000", 1000000000000)]
        public void Convert_DecimalToLong_ReturnsExpectedValue(string moneroString, long expectedPiconero)
        {
            decimal monero = decimal.Parse(moneroString, CultureInfo.InvariantCulture);
            long result = MoneroMoney.Convert(monero);
            Assert.Equal(expectedPiconero, result);
        }
        
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData(1)]
        [InlineData(123456789012)]
        [InlineData(1000000000000)]
        public void RoundTripConversion_LongToDecimalToLong_ReturnsOriginalValue(long piconero)
        {
            decimal monero = MoneroMoney.Convert(piconero);
            long convertedBack = MoneroMoney.Convert(monero);
            Assert.Equal(piconero, convertedBack);
        }
    }
}