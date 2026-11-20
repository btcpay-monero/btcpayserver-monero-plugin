using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net.Http;
using System.Threading.Tasks;

using BTCPayServer.Plugins.Monero.Configuration;

using Microsoft.Extensions.Logging;

using Monero.Common;
using Monero.Daemon.Common;
using Monero.Lws;

namespace BTCPayServer.Plugins.Monero.Services;

public interface IMoneroLwsProvider
{
    Task Login(string address, string viewKey);
    Task<MoneroSummary> UpdateSummary(string cryptoCode, string address = null, string viewKey = null);
    public bool IsAvailable(string cryptoCode);
    ConcurrentDictionary<string, MoneroSummary> Summaries { get; }
}

public class MoneroLwsProvider : IMoneroLwsProvider
{
    private readonly MoneroLwsService _lws;
    private readonly ILogger<MoneroLwsProvider> _logger;
    private readonly MoneroLikeConfiguration _moneroLikeConfiguration;
    private readonly EventAggregator _eventAggregator;
    private ImmutableDictionary<string, MoneroRpcConnection> DaemonRpcClients { get; }
    public ConcurrentDictionary<string, MoneroSummary> Summaries { get; } = new();

    public MoneroLwsProvider(MoneroLikeConfiguration moneroLikeConfiguration, ILogger<MoneroLwsProvider> logger,
        EventAggregator eventAggregator,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _moneroLikeConfiguration = moneroLikeConfiguration;
        _eventAggregator = eventAggregator;

        if (!moneroLikeConfiguration.MoneroLikeConfigurationItems.TryGetValue("XMR", out var item) ||
            item.LwsUri is null)
        {
            throw new InvalidOperationException("BTCPAY_XMR_LWS_URI is not configured");
        }

        DaemonRpcClients =
            _moneroLikeConfiguration.MoneroLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                pair => new MoneroRpcConnection(pair.Value.DaemonRpcUri, pair.Value.Username, pair.Value.Password,
                    httpClientFactory.CreateClient($"{pair.Key}client")));

        _lws = new MoneroLwsService(item.LwsUri, "", "", item.LwsUsername ?? "", item.LwsPassword ?? "");
    }

    public bool IsAvailable(string cryptoCode)
    {
        cryptoCode = cryptoCode.ToUpperInvariant();
        return Summaries.ContainsKey(cryptoCode) && IsAvailable(Summaries[cryptoCode]);
    }

    public bool IsAvailable(MoneroSummary summary)
    {
        return summary.Synced &&
               summary.LwsAvailable;
    }

    public async Task<MoneroSummary> UpdateSummary(string cryptoCode, string address, string viewKey)
    {
        if (!DaemonRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var daemonRpcClient))
        {
            return null;
        }

        var summary = new MoneroSummary();
        try
        {
            var daemonResult =
                await daemonRpcClient.SendCommandAsync<NoRequestModel, MoneroDaemonInfo>("get_info",
                    NoRequestModel.Instance);
            summary.TargetHeight = daemonResult.TargetHeight.GetValueOrDefault(0);
            summary.CurrentHeight = daemonResult.Height;
            summary.DaemonVersion = daemonResult.Version;
            summary.Restricted = daemonResult.IsRestricted;
            summary.TargetHeight = summary.TargetHeight == 0 ? summary.CurrentHeight : summary.TargetHeight;
            summary.Synced = !daemonResult.BusySyncing;
            summary.UpdatedAt = DateTime.UtcNow;
            summary.DaemonAvailable = true;
        }
        catch
        {
            summary.DaemonAvailable = false;
        }

        try
        {
            var lwsVersion = await _lws.GetVersion();
            summary.LwsVersion = lwsVersion.ServerVersion;
            summary.LwsAvailable = true;
        }
        catch
        {
            summary.LwsAvailable = false;
        }

        if (!string.IsNullOrEmpty(address) && !string.IsNullOrEmpty(viewKey))
        {
            try
            {
                var addressInfo = await _lws.GetAddressInfo(address, viewKey);
                summary.WalletHeight = checked((ulong)addressInfo.ScannedHeight);
                summary.WalletAvailable = true;
            }
            catch
            {
                summary.WalletAvailable = false;
            }
        }

        var changed = !Summaries.ContainsKey(cryptoCode) || IsAvailable(cryptoCode) != IsAvailable(summary);

        Summaries[cryptoCode] = summary;
        if (changed)
        {
            _eventAggregator.Publish(
                new MoneroDaemonStateChange { Summary = summary, CryptoCode = cryptoCode });
        }

        return summary;
    }

    public async Task Login(string address, string viewKey)
    {
        var response = await _lws.Login(address, viewKey, createAccount: true, generatedLocally: false);
        _logger.LogInformation("LWS login for {Address}: newAccount={New}, startHeight={Height}",
            address, response.NewAddress, response.StartHeight);
    }
}