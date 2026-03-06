using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.Monero.Configuration
{
    public class MoneroWalletState
    {
        public string ActiveWalletAddress { get; set; }

        public string ActiveWalletName => ActiveWalletAddress != null && Wallets.TryGetValue(ActiveWalletAddress, out var name) ? name : null;

        public DateTimeOffset? LastActivatedAt { get; set; }

        public bool IsInitialized => !string.IsNullOrEmpty(ActiveWalletAddress);

        public bool IsConnected { get; set; }

        public bool PasswordFileMigration { get; set; }

        public Dictionary<string, string> Wallets { get; set; } = [];
    }
}