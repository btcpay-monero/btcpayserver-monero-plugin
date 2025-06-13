using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Monero.RPC.Models
{
    public partial class CreateWalletRequest
    {
        [JsonProperty("filename")] public string Filename { get; set; }
        [JsonProperty("password")] public string Password { get; set; }
        [JsonProperty("language")] public string Language { get; set; }
    }
}