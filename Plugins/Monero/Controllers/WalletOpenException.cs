using System;

namespace BTCPayServer.Plugins.Monero.Controllers;

public class WalletOpenException(string message) : Exception(message);