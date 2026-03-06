using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Monero.RPC.Models
{
    public partial class CreateAddressResponse
    {
        [JsonProperty("address")] public string Address { get; set; }
        [JsonProperty("address_index")] public long AddressIndex { get; set; }
        [JsonProperty("address_indices")] public long[] AddressIndices { get; set; }
        [JsonProperty("addresses")] public string[] Addresses { get; set; }
    }
}