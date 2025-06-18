namespace BTCPayServer.Plugins.Monero.Payments
{
    public class MoneroPaymentPromptDetails
    {
        public long AccountIndex { get; set; }
        public long? InvoiceSettledConfirmationThreshold { get; set; }
    }
}