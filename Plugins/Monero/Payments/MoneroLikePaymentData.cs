namespace BTCPayServer.Plugins.Monero.Payments
{
    public class MoneroLikePaymentData
    {
        public long SubaddressIndex { get; set; }
        public long SubaccountIndex { get; set; }
        public ulong BlockHeight { get; set; }
        public int ConfirmationCount { get; set; }
        public string TransactionId { get; set; }
        public int? InvoiceSettledConfirmationThreshold { get; set; }
        public int LockTime { get; set; }
    }
}