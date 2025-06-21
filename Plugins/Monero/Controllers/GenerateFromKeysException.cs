using System;

namespace BTCPayServer.Plugins.Monero.Controllers;

public class GenerateFromKeysException(string message) : Exception(message);