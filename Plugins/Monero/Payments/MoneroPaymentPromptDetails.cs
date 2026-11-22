using BTCPayServer.Client.Models;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Plugins.Monero.Payments;

public class MoneroPaymentPromptDetails
{
    public long AccountIndex { get; set; }
    public int? InvoiceSettledConfirmationThreshold { get; set; }

    /// <summary>
    /// Overrides the store's network fee mode for this payment method. <c>null</c> uses the store's setting.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public NetworkFeeMode? NetworkFeeMode { get; set; }
}
