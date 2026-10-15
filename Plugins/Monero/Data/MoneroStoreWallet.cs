using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.Monero.Data;

public class MoneroStoreWallet
{
    [Key]
    [MaxLength(50)]
    public string StoreId { get; set; }

    [Required]
    [MaxLength(106)]
    public string PrimaryAddress { get; set; }

    [Required]
    [MaxLength(64)]
    public string PrivateViewKey { get; set; }
}