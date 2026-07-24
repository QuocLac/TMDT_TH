using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

[Table("ReturnRequests")]
[Index(nameof(Code), IsUnique = true)]
[Index(nameof(IdempotencyKey), IsUnique = true)]
[Index(nameof(OrderId), nameof(Status), nameof(RequestedAt))]
public sealed class ReturnRequest : BaseEntity
{
    public long Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    [Required, MaxLength(30)]
    public string Code { get; set; } = string.Empty;

    public ReturnRequestStatus Status { get; set; } = ReturnRequestStatus.Requested;

    [Required, MaxLength(50)]
    public string ReasonCode { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string ReasonText { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string RequestedBy { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    public DateTime RequestedAt { get; set; }
    public DateTime ReturnWindowExpiresAt { get; set; }

    [MaxLength(100)]
    public string? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    [MaxLength(500)]
    public string? ReviewNote { get; set; }

    public DateTime? ApprovedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public DateTime? InspectedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ReturnInspectionResult? InspectionResult { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public ICollection<ReturnItem> Items { get; set; } = [];
    public ICollection<ReturnInspection> Inspections { get; set; } = [];
    public ICollection<ReturnEvidence> Evidence { get; set; } = [];
    public ICollection<Shipment> Shipments { get; set; } = [];

    public ReturnRefundAccount? RefundAccount { get; set; }
}
