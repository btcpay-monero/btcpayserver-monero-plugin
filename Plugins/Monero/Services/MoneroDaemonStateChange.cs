namespace BTCPayServer.Plugins.Monero.Services;

public class MoneroDaemonStateChange
{
    public string CryptoCode { get; set; }
    public MoneroSummary Summary { get; set; }
}