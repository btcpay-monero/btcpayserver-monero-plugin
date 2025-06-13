using System.Linq;
namespace BTCPayServer.Plugins.Monero.RPC
{
    public class MoneroEvent
    {
        public string BlockHash { get; set; }
        public string TransactionHash { get; set; }
        public string CryptoCode { get; set; }

        public override string ToString()
        {
            var txUpdate = string.IsNullOrEmpty(TransactionHash) ? string.Empty : "Tx Update";
            var newBlock = string.IsNullOrEmpty(BlockHash) ? string.Empty : "New Block";

            var eventDescription = string.Join(" ", new[] { txUpdate, newBlock }.Where(desc => !string.IsNullOrEmpty(desc)));

            return $"{CryptoCode}: {eventDescription} ({TransactionHash ?? string.Empty}{BlockHash ?? string.Empty})";
        }
    }
}