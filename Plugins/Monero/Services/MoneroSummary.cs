using System;

namespace BTCPayServer.Plugins.Monero.Services;

public class MoneroSummary
{
    public bool Synced { get; set; }
    public long CurrentHeight { get; set; }
    public ulong WalletHeight { get; set; }
    public long TargetHeight { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool DaemonAvailable { get; set; }
    public string DaemonVersion { get; set; }
    public bool Restricted { get; set; }
    public bool WalletAvailable { get; set; }
    public bool LwsAvailable { get; set; }
}