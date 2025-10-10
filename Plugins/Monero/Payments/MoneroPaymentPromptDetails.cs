namespace BTCPayServer.Plugins.Monero.Payments
{
    public class MoneroPaymentPromptDetails
    {
        public long AccountIndex { get; set; }
        public int? InvoiceSettledConfirmationThreshold { get; set; }
    }
}