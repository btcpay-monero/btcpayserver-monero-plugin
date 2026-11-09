using System;
using System.Threading.Tasks;

using BTCPayServer.Plugins.Monero.Configuration;

using Microsoft.Extensions.Logging;

using Monero.Lws;

namespace BTCPayServer.Plugins.Monero.Services;

public interface IMoneroLwsProvider
{
    Task Login(string address, string viewKey);
}

public class MoneroLwsProvider : IMoneroLwsProvider
{
    private readonly MoneroLwsService _lws;
    private readonly ILogger<MoneroLwsProvider> _logger;

    public MoneroLwsProvider(MoneroLikeConfiguration config, ILogger<MoneroLwsProvider> logger)
    {
        _logger = logger;

        if (!config.MoneroLikeConfigurationItems.TryGetValue("XMR", out var item) || item.LwsUri is null)
        {
            throw new InvalidOperationException("BTCPAY_XMR_LWS_URI is not configured");
        }

        _lws = new MoneroLwsService(item.LwsUri, "", "", item.LwsUsername ?? "", item.LwsPassword ?? "");
    }

    public async Task Login(string address, string viewKey)
    {
        var response = await _lws.Login(address, viewKey, createAccount: true, generatedLocally: false);
        _logger.LogInformation("LWS login for {Address}: newAccount={New}, startHeight={Height}",
            address, response.NewAddress, response.StartHeight);
    }
}