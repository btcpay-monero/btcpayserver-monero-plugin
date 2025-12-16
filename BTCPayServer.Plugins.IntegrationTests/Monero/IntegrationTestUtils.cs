using System.Diagnostics;

using BTCPayServer.Plugins.Monero.Services;
using BTCPayServer.Tests;

using Microsoft.Extensions.Logging;

using Npgsql;

namespace BTCPayServer.Plugins.IntegrationTests.Monero;

public static class IntegrationTestUtils
{

    private static readonly ILogger Logger = LoggerFactory
        .Create(builder => builder.AddConsole())
        .CreateLogger("IntegrationTestUtils");

    public static async Task CleanUpAsync(PlaywrightTester playwrightTester)
    {
        MoneroRPCProvider moneroRpcProvider = playwrightTester.Server.PayTester.GetService<MoneroRPCProvider>();
        if (moneroRpcProvider.IsAvailable("XMR"))
        {
            await moneroRpcProvider.CloseWallet("XMR");
        }

        if (playwrightTester.Server.PayTester.InContainer)
        {
            moneroRpcProvider.DeleteWallet();
            await DropDatabaseAsync(
                "btcpayserver",
                "Host=postgres;Port=5432;Username=postgres;Database=postgres");
        }
        else
        {
            await RemoveWalletFromLocalDocker();
            await DropDatabaseAsync(
                "btcpayserver",
                "Host=localhost;Port=39372;Username=postgres;Database=postgres");
        }
    }

    private static async Task DropDatabaseAsync(string dbName, string connectionString)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await new NpgsqlCommand($"""
                                     SELECT pg_terminate_backend(pid)
                                     FROM pg_stat_activity
                                     WHERE datname = '{dbName}' 
                                       AND pid <> pg_backend_pid();
                                     """, conn).ExecuteNonQueryAsync();
            var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {dbName};", conn);
            await cmd.ExecuteNonQueryAsync();
            Logger.LogInformation("Database {DbName} dropped successfully.", dbName);
        }
        catch (Exception ex)
        {
            Logger.LogError("Failed to drop database {DbName}: {ExMessage}", dbName, ex.Message);
        }
    }

    private static async Task RemoveWalletFromLocalDocker()
    {
        try
        {
            var removeWalletFromDocker = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "exec xmr_wallet sh -c \"rm -rf /wallet/*\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(removeWalletFromDocker);
            if (process is null)
            {
                Logger.LogWarning("Failed to start docker process for wallet cleanup.");
                return;
            }

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                Logger.LogInformation("Docker wallet cleanup output: {Output}", stdout);
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Logger.LogWarning("Docker wallet cleanup error output: {Error}", stderr);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Wallet cleanup via Docker failed.");
        }
    }
}