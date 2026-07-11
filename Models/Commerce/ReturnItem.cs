using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

[Table("ReturnItems")]
[Index(nameof(ReturnRequestId), nameof(OrderItemId), IsUnique = true)]
[Index(nameof(OrderItemId))]
public sealed class ReturnItem
{
    public long Id { get; set; }

    public long ReturnRequestId { get; set; }
    public ReturnRequest ReturnRequest { get; set; } = null!;

    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;

    public int RequestedQuantity { get; set; }
    public int ApprovedQuantity { get; set; }
    public int ReceivedQuantity { get; set; }
    public int AcceptedQuantity { get; set; }
    public int RejectedQuantity { get; set; }
    public int RestockQuantity { get; set; }
    public int WriteOffQuantity { get; set; }

    public ReturnItemCondition? ConditionCode { get; set; }

    [MaxLength(500)]
    public string? InspectionNote { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal RefundAmount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
