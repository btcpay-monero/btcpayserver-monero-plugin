using System;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Plugins.Monero.Configuration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Monero.Services
{
    public class MoneroLikeSummaryUpdaterHostedService : IHostedService
    {
        private readonly MoneroRpcProvider _MoneroRpcProvider;
        private readonly MoneroLikeConfiguration _moneroLikeConfiguration;
        private readonly ILogger<MoneroLikeSummaryUpdaterHostedService> _logger;

        private CancellationTokenSource _Cts;
        public MoneroLikeSummaryUpdaterHostedService(MoneroRpcProvider moneroRpcProvider, MoneroLikeConfiguration moneroLikeConfiguration, ILogger<MoneroLikeSummaryUpdaterHostedService> logger)
        {
            _MoneroRpcProvider = moneroRpcProvider;
            _moneroLikeConfiguration = moneroLikeConfiguration;
            _logger = logger;
        }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            foreach (var moneroLikeConfigurationItem in _moneroLikeConfiguration.MoneroLikeConfigurationItems)
            {
                _ = StartLoop(_Cts.Token, moneroLikeConfigurationItem.Key);
            }
            return Task.CompletedTask;
        }

        private async Task StartLoop(CancellationToken cancellation, string cryptoCode)
        {
            _logger.LogInformation($"Starting listening Monero-like daemons ({cryptoCode})");
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        await _MoneroRpcProvider.UpdateSummary(cryptoCode);
                        if (_MoneroRpcProvider.IsAvailable(cryptoCode))
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
                        _logger.LogError(ex, $"Unhandled exception in Summary updater ({cryptoCode})");
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
}