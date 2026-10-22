using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Monero.Services;

public class MoneroLwsLoadUpService(
    ILogger<MoneroLwsLoadUpService> logger,
    IMoneroLwsProvider lws) : IHostedService
{
    private const string CryptoCode = "XMR";

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Attempt to load existing wallet with LWS");

            await lws.Login();
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to reach LWS for {CryptoCode}. Error Message: {ErrorMessage}",
                CryptoCode, ex.Message);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}