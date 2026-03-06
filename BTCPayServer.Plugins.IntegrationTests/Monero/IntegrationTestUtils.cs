using System.Diagnostics;

using BTCPayServer.Plugins.Monero.Services;
using BTCPayServer.Tests;

using Microsoft.Extensions.Logging;

using Mono.Unix.Native;

using Npgsql;

using static Mono.Unix.Native.Syscall;

namespace BTCPayServer.Plugins.IntegrationTests.Monero;

public static class IntegrationTestUtils
{

    private static readonly ILogger Logger = LoggerFactory
        .Create(builder => builder.AddConsole())
        .CreateLogger("IntegrationTestUtils");

    private static readonly string ContainerWalletDir =
        Environment.GetEnvironmentVariable("BTCPAY_XMR_WALLET_DAEMON_WALLETDIR") ?? "/wallet";

    public static async Task CleanUpAsync(PlaywrightTester playwrightTester, bool deleteWalletFiles = true)
    {
        var moneroRpcProvider = playwrightTester.Server.PayTester.GetService<MoneroRpcProvider>();
        await moneroRpcProvider.CloseWallet("XMR");

        var walletService = playwrightTester.Server.PayTester.GetService<MoneroWalletService>();
        walletService.GetWalletState().IsConnected = false;

        if (playwrightTester.Server.PayTester.InContainer)
        {
            if (deleteWalletFiles)
            {
                moneroRpcProvider.DeleteAllWallets();
            }
            await DropDatabaseAsync(
                "btcpayserver",
                "Host=postgres;Port=5432;Username=postgres;Database=postgres");
        }
        else
        {
            if (deleteWalletFiles)
            {
                await RemoveWalletFromLocalDocker();
            }
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

    public static async Task CopyWalletFilesToMoneroRpcDirAsync(PlaywrightTester playwrightTester, String walletDir)
    {
        Logger.LogInformation("Starting to copy wallet files");
        if (playwrightTester.Server.PayTester.InContainer)
        {
            CopyWalletFilesInContainer(walletDir);
        }
        else
        {
            await CopyWalletFilesToLocalDocker(walletDir);
        }
    }

    private static void CopyWalletFilesInContainer(String walletDir)
    {
        try
        {
            CopyWalletFile("wallet", walletDir);
            CopyWalletFile("wallet.keys", walletDir);
            CopyWalletFile("password", walletDir);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to copy wallet files to the Monero directory.");
        }
    }

    private static void CopyWalletFile(string name, string walletDir)
    {
        var resourceWalletDir = Path.Combine(AppContext.BaseDirectory, "Resources", walletDir);

        var src = Path.Combine(resourceWalletDir, name);
        var dst = Path.Combine(ContainerWalletDir, name);

        if (!File.Exists(src))
        {
            return;
        }

        File.Copy(src, dst, overwrite: true);

        // monero ownership
        if (chown(dst, 980, 980) == 0)
        {
            return;
        }

        Logger.LogError("chown failed for {File}. errno={Errno}", dst, Stdlib.GetLastError());
    }


    private static async Task CopyWalletFilesToLocalDocker(String walletDir)
    {
        try
        {
            var fullWalletDir = Path.Combine(AppContext.BaseDirectory, "Resources", walletDir);

            await RunProcessAsync("docker",
                $"cp \"{Path.Combine(fullWalletDir, "wallet")}\" xmr_wallet:/wallet/wallet");

            await RunProcessAsync("docker",
                $"cp \"{Path.Combine(fullWalletDir, "wallet.keys")}\" xmr_wallet:/wallet/wallet.keys");

            await RunProcessAsync("docker",
                $"cp \"{Path.Combine(fullWalletDir, "password")}\" xmr_wallet:/wallet/password");

            await RunProcessAsync("docker",
                "exec xmr_wallet chown monero:monero /wallet/wallet /wallet/wallet.keys /wallet/password");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to copy wallet files to the Monero directory.");
        }
    }

    static async Task RunProcessAsync(string fileName, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)!;
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception(await process.StandardError.ReadToEndAsync());
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

    private static void DeleteWalletInContainer()
    {
        try
        {
            var walletFile = Path.Combine(ContainerWalletDir, "wallet");
            var keysFile = walletFile + ".keys";
            var passwordFile = Path.Combine(ContainerWalletDir, "password");

            if (File.Exists(walletFile))
            {
                File.Delete(walletFile);
            }

            if (File.Exists(keysFile))
            {
                File.Delete(keysFile);
            }

            if (File.Exists(passwordFile))
            {
                File.Delete(passwordFile);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to delete wallet files in directory {Dir}", ContainerWalletDir);
        }
    }
}