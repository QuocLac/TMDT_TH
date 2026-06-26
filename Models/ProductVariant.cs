using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

public sealed class ProductVariant : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string SKU { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Color { get; set; }

    [MaxLength(50)]
    public string? Size { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CurrentPrice { get; set; }

    public EffectivePriceSourceType CurrentPriceSourceType { get; set; }
        = EffectivePriceSourceType.ListPrice;

    public int? CurrentPriceSourceId { get; set; }

    public DateTime? CurrentPriceEffectiveFrom { get; set; }

    public DateTime? CurrentPriceEffectiveTo { get; set; }

    public int StockQuantity { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; } = true;

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public ICollection<PriceHistory> PriceHistories { get; set; } = [];

    public ICollection<PriceCampaignItem> CampaignItems { get; set; } = [];
}
