namespace BTCPayServer.Plugins.Monero.Payments
{
    public class MoneroLikeOnChainPaymentMethodDetails
    {
        public long AccountIndex { get; set; }
        public long AddressIndex { get; set; }
        public long? InvoiceSettledConfirmationThreshold { get; set; }
    }
}