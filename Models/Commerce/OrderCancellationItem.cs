using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace WebApplication2.Models;

[Table("OrderCancellationItems")]
[Index(nameof(CancellationRequestId), nameof(OrderItemId), IsUnique = true)]
[Index(nameof(OrderItemId))]
public sealed class OrderCancellationItem
{
    public long Id { get; set; }

    public long CancellationRequestId { get; set; }
    public OrderCancellationRequest CancellationRequest { get; set; } = null!;

    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;

    public int RequestedQuantity { get; set; }

    public int ApprovedQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal RefundAmount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
