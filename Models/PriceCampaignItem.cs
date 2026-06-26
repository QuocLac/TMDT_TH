using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

public sealed class PriceCampaignItem
{
    public int CampaignId { get; set; }

    public PriceCampaign Campaign { get; set; } = null!;

    public int VariantId { get; set; }

    public ProductVariant Variant { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    public decimal ListPriceSnapshot { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal EffectivePriceSnapshot { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PreviousEffectivePriceSnapshot { get; set; }

    public PriceAdjustmentType AdjustmentType { get; set; } = PriceAdjustmentType.FixedPrice;

    [Column(TypeName = "decimal(18,2)")]
    public decimal AdjustmentValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal NewPrice { get; set; }

    [Required, MaxLength(3)]
    public string Currency { get; set; } = "VND";
}
