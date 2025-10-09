namespace BTCPayServer.Plugins.Monero.Payments
{
    public class MoneroLikePaymentData
    {
        public uint? SubaddressIndex { get; set; }
        public uint? SubaccountIndex { get; set; }
        public ulong BlockHeight { get; set; }
        public ulong ConfirmationCount { get; set; }
        public string TransactionId { get; set; }
        public long? InvoiceSettledConfirmationThreshold { get; set; }

        public ulong LockTime { get; set; }
    }
}