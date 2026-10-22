using System;
using System.Threading.Tasks;

using BTCPayServer.Plugins.Monero.Configuration;

using Microsoft.Extensions.Logging;

using Monero.Lws;

namespace BTCPayServer.Plugins.Monero.Services;

public interface IMoneroLwsProvider
{
    Task Login();
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

        _lws = new MoneroLwsService(item.LwsUri, "lws", "admin", item.Username ?? "", item.Password ?? "");
    }

    public Task Login()
    {
        // TODO implement proper wallet creation / login _lws.Login();
        return Task.CompletedTask;
    }
}