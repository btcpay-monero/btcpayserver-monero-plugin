namespace BTCPayServer.Plugins.Monero.Payments
{
    public class MoneroLikeOnChainPaymentMethodDetails
    {
        public long AccountIndex { get; set; }
        public uint AddressIndex { get; set; }
        public int? InvoiceSettledConfirmationThreshold { get; set; }
    }
}