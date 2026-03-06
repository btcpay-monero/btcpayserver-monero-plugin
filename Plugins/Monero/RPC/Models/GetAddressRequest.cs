using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Monero.RPC.Models;

public class GetAddressRequest
{
    [JsonProperty("account_index")] public int AccountIndex { get; set; }
    [JsonProperty("address_index")] public int[] AddressIndex { get; set; }
}

public class GetAddressResponse
{
    [JsonProperty("address")] public string Address { get; set; }
    [JsonProperty("addresses")] public AddressInfo[] Addresses { get; set; }
}

public class AddressInfo
{
    [JsonProperty("address")] public string Address { get; set; }
    [JsonProperty("address_index")] public long AddressIndex { get; set; }
    [JsonProperty("label")] public string Label { get; set; }
    [JsonProperty("used")] public bool Used { get; set; }
}