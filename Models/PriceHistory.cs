using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

/// <summary>
/// Sổ sự kiện giá bất biến của một biến thể.
/// CreatedAt là thời điểm sự kiện được hệ thống ghi nhận.
/// EffectiveFrom/EffectiveTo là khoảng thời gian giá có hiệu lực trong nghiệp vụ.
/// </summary>
public sealed class PriceHistory : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int ProductVariantId { get; set; }

    public ProductVariant ProductVariant { get; set; } = null!;

    public PriceHistoryKind PriceKind { get; set; }
        = PriceHistoryKind.EffectivePrice;

    [Required, MaxLength(3)]
    public string Currency { get; set; } = "VND";

    [Column(TypeName = "decimal(18,2)")]
    public decimal OldPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal NewPrice { get; set; }

    public PriceHistoryEventType EventType { get; set; }
        = PriceHistoryEventType.Legacy;

    public PriceChangeSourceType SourceType { get; set; }
        = PriceChangeSourceType.Legacy;

    public int? SourceId { get; set; }

    [MaxLength(64)]
    public string? CorrelationId { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    public DateTime? EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    [Required, MaxLength(100)]
    public string ChangedBy { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string Note { get; set; } = string.Empty;
}
