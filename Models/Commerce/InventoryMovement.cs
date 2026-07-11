using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

public sealed class InventoryMovement
{
    public long Id { get; set; }

    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = null!;

    public InventoryMovementType MovementType { get; set; }
    public int QuantityDelta { get; set; }
    public int QuantityBefore { get; set; }
    public int QuantityAfter { get; set; }

    [Required, MaxLength(50)]
    public string ReferenceType { get; set; } = string.Empty;

    public long ReferenceId { get; set; }

    [Required, MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
