using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

public sealed class PaymentTransaction : BaseEntity
{
    public long Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    [Required, MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string Method { get; set; } = string.Empty;

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public int AttemptNumber { get; set; } = 1;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Required, MaxLength(3)]
    public string Currency { get; set; } = "VND";

    [Required, MaxLength(100)]
    public string MerchantReference { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? ProviderTransactionId { get; set; }

    [Required, MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? RequestPayload { get; set; }
    public string? ResponsePayload { get; set; }

    [MaxLength(100)]
    public string? FailureCode { get; set; }

    [MaxLength(500)]
    public string? FailureMessage { get; set; }

    public DateTime? CompletedAt { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
