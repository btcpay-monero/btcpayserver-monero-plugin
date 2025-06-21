using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Monero.RPC.Models;

public class GenerateFromKeysRequest
{
    [JsonProperty("address")] public string PrimaryAddress { get; set; }
    [JsonProperty("viewkey")] public string PrivateViewKey { get; set; }
    [JsonProperty("filename")] public string WalletFileName { get; set; }
    [JsonProperty("restore_height")] public int RestoreHeight { get; set; }
    [JsonProperty("password")] public string Password { get; set; }
}