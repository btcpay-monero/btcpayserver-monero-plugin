using System;

namespace BTCPayServer.Plugins.Monero.Utils;

public enum MoneroBackend
{
    WalletRpc,
    LwsServer
}

public static class MoneroBackendResolver
{
    private const string EnvVar = "BTCPAY_XMR_BACKEND";

    public static MoneroBackend Resolve()
    {
        var value = Environment.GetEnvironmentVariable(EnvVar)?.Trim();

        if (string.IsNullOrEmpty(value) ||
            value.Equals("monero-wallet-rpc", StringComparison.OrdinalIgnoreCase))
        {
            return MoneroBackend.WalletRpc;
        }

        if (value.Equals("monero-lws-server", StringComparison.OrdinalIgnoreCase))
        {
            return MoneroBackend.LwsServer;
        }

        throw new InvalidOperationException(
            $"Invalid {EnvVar} '{value}'. Expected 'monero-wallet-rpc' or 'monero-lws-server'.");
    }
}