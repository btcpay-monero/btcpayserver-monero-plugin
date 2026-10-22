using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.Monero.Configuration;

public class MoneroLikeConfiguration
{
    public Dictionary<string, MoneroLikeConfigurationItem> MoneroLikeConfigurationItems { get; set; } = [];
}

public class MoneroLikeConfigurationItem
{
    // shared
    public Uri DaemonRpcUri { get; set; }

    // wallet-rpc backend
    public Uri InternalWalletRpcUri { get; set; }
    public string WalletDirectory { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }

    // lws backend
    public Uri LwsUri { get; set; }
    public string LwsUsername { get; set; }
    public string LwsPassword { get; set; }
}