using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Monero.RPC.Models;

public class GenerateFromKeysResult
{
    [JsonProperty("address")] public string ViewWalletAddress { get; set; }
    [JsonProperty("info")] public string CreationInfo { get; set; }
}