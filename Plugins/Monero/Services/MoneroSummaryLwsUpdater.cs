using System;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Logging;
using BTCPayServer.Plugins.Monero.Configuration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Monero.Services;

public class MoneroSummaryLwsUpdater : IHostedService
{
    private readonly IMoneroLwsProvider _MoneroLwsProvider;
    private readonly MoneroLikeConfiguration _moneroLikeConfiguration;

    public Logs Logs { get; }

    private CancellationTokenSource _Cts;
    public MoneroSummaryLwsUpdater(IMoneroLwsProvider moneroLwsProvider, MoneroLikeConfiguration moneroLikeConfiguration, Logs logs)
    {
        _MoneroLwsProvider = moneroLwsProvider;
        _moneroLikeConfiguration = moneroLikeConfiguration;
        Logs = logs;
    }
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        foreach (var moneroLikeConfigurationItem in _moneroLikeConfiguration.MoneroLikeConfigurationItems)
        {
            _ = StartLoop(moneroLikeConfigurationItem.Key, _Cts.Token);
        }
        return Task.CompletedTask;
    }

    private async Task StartLoop(string cryptoCode, CancellationToken cancellation)
    {
        Logs.PayServer.LogInformation("Starting listening Monero-like daemons ({CryptoCode})", cryptoCode);
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    await _MoneroLwsProvider.UpdateSummary(cryptoCode);
                    if (_MoneroLwsProvider.IsAvailable(cryptoCode))
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), cancellation);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
                    }
                }
                catch (Exception ex) when (!cancellation.IsCancellationRequested)
                {
                    Logs.PayServer.LogError(ex, "Unhandled exception in Summary updater ({CryptoCode})", cryptoCode);
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
                }
            }
        }
        catch when (cancellation.IsCancellationRequested)
        {
            // ignored
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _Cts?.Cancel();
        _Cts?.Dispose();
        return Task.CompletedTask;
    }
}