using System.Collections.Generic;
using System.Linq;

using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.Monero.Services
{
    public class MoneroSyncSummaryProvider : ISyncSummaryProvider
    {
        private readonly MoneroRPCProvider _moneroRpcProvider;

        public MoneroSyncSummaryProvider(MoneroRPCProvider moneroRpcProvider)
        {
            _moneroRpcProvider = moneroRpcProvider;
        }

        public bool AllAvailable()
        {
            return _moneroRpcProvider.Summaries.All(pair => pair.Value.DaemonAvailable);
        }

        public string Partial { get; } = "/Views/Monero/MoneroSyncSummary.cshtml";
        public IEnumerable<ISyncStatus> GetStatuses()
        {
            return _moneroRpcProvider.Summaries.Select(pair => new MoneroSyncStatus()
            {
                Summary = pair.Value,
                PaymentMethodId = PaymentMethodId.Parse(pair.Key).ToString()
            });
        }
    }

    public class MoneroSyncStatus : SyncStatus, ISyncStatus
    {
        public override bool Available
        {
            get
            {
                return Summary?.WalletAvailable ?? false;
            }
        }

        public MoneroRPCProvider.MoneroLikeSummary Summary { get; set; }
    }
}