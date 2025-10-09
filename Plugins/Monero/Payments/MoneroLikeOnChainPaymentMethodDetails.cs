namespace BTCPayServer.Plugins.Monero.Payments
{
    public class MoneroLikeOnChainPaymentMethodDetails
    {
        public uint AccountIndex { get; set; }
        public uint AddressIndex { get; set; }
        public long? InvoiceSettledConfirmationThreshold { get; set; }
    }
}