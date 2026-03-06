using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Monero.RPC.Models;

public class ChangeWalletPasswordRequest
{
    [JsonProperty("old_password")] public string OldPassword { get; set; }
    [JsonProperty("new_password")] public string NewPassword { get; set; }
}