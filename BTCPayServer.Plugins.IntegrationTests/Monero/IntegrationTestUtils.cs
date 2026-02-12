using System.Diagnostics;
using System.Text;
using System.Text.Json;

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

    private static readonly bool RunsInContainer =
        bool.Parse(Environment.GetEnvironmentVariable("TESTS_INCONTAINER") ?? "false");

    private static readonly string ContainerWalletDir =
        Environment.GetEnvironmentVariable("BTCPAY_XMR_WALLET_DAEMON_WALLETDIR") ?? "/wallet";

    public static async Task CleanUpAsync(PlaywrightTester playwrightTester)
    {
        MoneroRpcProvider moneroRpcProvider = playwrightTester.Server.PayTester.GetService<MoneroRpcProvider>();
        if (moneroRpcProvider.IsAvailable("XMR"))
        {
            await moneroRpcProvider.CloseWallet("XMR");
        }

        if (RunsInContainer)
        {
            DeleteWalletInContainer();
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

    private static async Task CopyViewWalletPasswordFileToMoneroRpcDirAsync(String walletDir)
    {
        Logger.LogInformation("Starting to copy password file");
        if (RunsInContainer)
        {
            CopyViewWalletPasswordFileInContainer(walletDir);
        }
        else
        {
            await CopyViewWalletPasswordFileToLocalDocker(walletDir);
        }
    }

    private static void CopyViewWalletPasswordFileInContainer(String walletDir)
    {
        try
        {
            CopyWalletFile("password", walletDir);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to copy password file to the Monero directory.");
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


    private static async Task CopyViewWalletPasswordFileToLocalDocker(String walletDir)
    {
        try
        {
            var fullWalletDir = Path.Combine(AppContext.BaseDirectory, "Resources", walletDir);

            await RunProcessAsync("docker",
                $"cp \"{Path.Combine(fullWalletDir, "password")}\" xmr_wallet:/wallet/password");

            await RunProcessAsync("docker",
                "exec xmr_wallet chown monero:monero /wallet/password");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to copy password file to the Monero directory.");
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

    public static async Task CreateTestXmrWalletFilesViaRpc(string password)
    {
        var host = RunsInContainer ? "xmr_wallet" : "localhost";

        Logger.LogInformation("Creating wallet files via RPC on {Host}", host);

        using var httpClient = new HttpClient();
        var uri = new UriBuilder { Scheme = "http", Host = host, Port = 18082 }.Uri;
        httpClient.BaseAddress = uri;

        var requestPayload = new
        {
            id = "0",
            jsonrpc = "2.0",
            method = "generate_from_keys",
            @params = new
            {
                address =
                    "43Pnj6ZKGFTJhaLhiecSFfLfr64KPJZw7MyGH73T6PTDekBBvsTAaWEUSM4bmJqDuYLizhA13jQkMRPpz9VXBCBqQQb6y5L",
                viewkey = "1bfa03b0c78aa6bc8292cf160ec9875657d61e889c41d0ebe5c54fd3a2c4b40e",
                filename = "wallet",
                restore_height = 0,
                password
            }
        };

        var json = JsonSerializer.Serialize(requestPayload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync("/json_rpc", content);
        response.EnsureSuccessStatusCode();
    }

    public static async Task CreateTestXmrWalletWithPasswordAsync(string walletPassword, string viewWalletPasswordFile)
    {
        await CreateTestXmrWalletFilesViaRpc(walletPassword);

        await CopyViewWalletPasswordFileToMoneroRpcDirAsync(viewWalletPasswordFile);
    }

}