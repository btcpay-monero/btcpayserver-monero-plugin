using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;

using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Monero.Configuration;
using BTCPayServer.Plugins.Monero.Payments;
using BTCPayServer.Plugins.Monero.Services;
using BTCPayServer.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NBitcoin;

using NBXplorer;

namespace BTCPayServer.Plugins.Monero;

public class MoneroPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.1.0" }
    };

    public override void Execute(IServiceCollection services)
    {
        var pluginServices = (PluginServiceCollection)services;
        var prov = pluginServices.BootstrapServices.GetRequiredService<NBXplorerNetworkProvider>();
        var chainName = prov.NetworkType;

        var network = new MoneroLikeSpecificBtcPayNetwork()
        {
            CryptoCode = "XMR",
            DisplayName = "Monero",
            Divisibility = 12,
            DefaultRateRules = new[]
            {
                    "XMR_X = XMR_BTC * BTC_X",
                    "XMR_BTC = kraken(XMR_BTC)"
                },
            CryptoImagePath = "monero.svg",
            UriScheme = "monero"
        };
        var blockExplorerLink = chainName == ChainName.Mainnet
                    ? "https://www.exploremonero.com/transaction/{0}"
                    : "https://testnet.xmrchain.net/tx/{0}";
        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("XMR");
        services.AddDefaultPrettyName(pmi, network.DisplayName);
        services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(pmi, new SimpleTransactionLinkProvider(blockExplorerLink));


        services.AddSingleton(provider =>
                ConfigureMoneroLikeConfiguration(provider));
        services.AddHttpClient("XMRclient")
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                var configuration = provider.GetRequiredService<MoneroLikeConfiguration>();
                if (!configuration.MoneroLikeConfigurationItems.TryGetValue("XMR", out var xmrConfig) || xmrConfig.Username is null || xmrConfig.Password is null)
                {
                    return new HttpClientHandler();
                }
                return new HttpClientHandler
                {
                    Credentials = new NetworkCredential(xmrConfig.Username, xmrConfig.Password),
                    PreAuthenticate = true
                };
            });
        services.AddSingleton<MoneroRPCProvider>();
        services.AddHostedService<MoneroLikeSummaryUpdaterHostedService>();
        services.AddHostedService<MoneroListener>();
        services.AddSingleton<IPaymentMethodHandler>(provider =>
                (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(MoneroLikePaymentMethodHandler), new object[] { network }));
        services.AddSingleton<IPaymentLinkExtension>(provider =>
(IPaymentLinkExtension)ActivatorUtilities.CreateInstance(provider, typeof(MoneroPaymentLinkExtension), new object[] { network, pmi }));
        services.AddSingleton<ICheckoutModelExtension>(provider =>
(ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(MoneroCheckoutModelExtension), new object[] { network, pmi }));

        services.AddSingleton<ICheckoutCheatModeExtension>(provider =>
            (ICheckoutCheatModeExtension)ActivatorUtilities.CreateInstance(provider, typeof(MoneroCheckoutCheatModeExtension), new object[] { network, pmi }));

        services.AddUIExtension("store-nav", "/Views/Monero/StoreNavMoneroExtension.cshtml");
        services.AddUIExtension("store-wallets-nav", "/Views/Monero/StoreWalletsNavMoneroExtension.cshtml");
        services.AddUIExtension("store-invoices-payments", "/Views/Monero/ViewMoneroLikePaymentData.cshtml");
        services.AddSingleton<ISyncSummaryProvider, MoneroSyncSummaryProvider>();
    }
    class SimpleTransactionLinkProvider : DefaultTransactionLinkProvider
    {
        public SimpleTransactionLinkProvider(string blockExplorerLink) : base(blockExplorerLink)
        {
        }

        public override string GetTransactionLink(string paymentId)
        {
            if (string.IsNullOrEmpty(BlockExplorerLink))
            {
                return null;
            }
            return string.Format(CultureInfo.InvariantCulture, BlockExplorerLink, paymentId);
        }
    }

    private static MoneroLikeConfiguration ConfigureMoneroLikeConfiguration(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetService<IConfiguration>();
        var btcPayNetworkProvider = serviceProvider.GetService<BTCPayNetworkProvider>();
        var result = new MoneroLikeConfiguration();

        var supportedNetworks = btcPayNetworkProvider.GetAll()
            .OfType<MoneroLikeSpecificBtcPayNetwork>();

        foreach (var moneroLikeSpecificBtcPayNetwork in supportedNetworks)
        {
            var daemonUri =
                configuration.GetOrDefault<Uri>($"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_daemon_uri",
                    null);
            var walletDaemonUri =
                configuration.GetOrDefault<Uri>(
                    $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_uri", null);
            var cashCowWalletDaemonUri =
                configuration.GetOrDefault<Uri>(
                    $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_cashcow_wallet_daemon_uri", null);
            var walletDaemonWalletDirectory =
                configuration.GetOrDefault<string>(
                    $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_walletdir", null);
            var daemonUsername =
                configuration.GetOrDefault<string>(
                    $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_daemon_username", null);
            var daemonPassword =
                configuration.GetOrDefault<string>(
                    $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_daemon_password", null);
            if (daemonUri == null || walletDaemonUri == null)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<MoneroPlugin>>();
                var cryptoCode = moneroLikeSpecificBtcPayNetwork.CryptoCode.ToUpperInvariant();
                if (daemonUri is null)
                {
                    logger.LogWarning($"BTCPAY_{cryptoCode}_DAEMON_URI is not configured");
                }
                if (walletDaemonUri is null)
                {
                    logger.LogWarning($"BTCPAY_{cryptoCode}_WALLET_DAEMON_URI is not configured");
                }
                logger.LogWarning($"{cryptoCode} got disabled as it is not fully configured.");
            }
            else
            {
                result.MoneroLikeConfigurationItems.Add(moneroLikeSpecificBtcPayNetwork.CryptoCode, new MoneroLikeConfigurationItem()
                {
                    DaemonRpcUri = daemonUri,
                    Username = daemonUsername,
                    Password = daemonPassword,
                    InternalWalletRpcUri = walletDaemonUri,
                    WalletDirectory = walletDaemonWalletDirectory,
                    CashCowWalletRpcUri = cashCowWalletDaemonUri,
                });
            }
        }
        return result;
    }
}