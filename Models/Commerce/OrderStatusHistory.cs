using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

public sealed class OrderStatusHistory
{
    public long Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public OrderHistoryCategory Category { get; set; }

    [MaxLength(50)]
    public string? FromStatus { get; set; }

    [Required, MaxLength(50)]
    public string ToStatus { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required, MaxLength(100)]
    public string ChangedBy { get; set; } = string.Empty;

    public bool CustomerVisible { get; set; }
    public DateTime OccurredAt { get; set; }

    [MaxLength(64)]
    public string? CorrelationId { get; set; }
}
