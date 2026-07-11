using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

public sealed class IntegrationOutboxMessage : BaseEntity
{
    public long Id { get; set; }

    [Required, MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string MessageType { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string AggregateType { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string AggregateId { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    public IntegrationOutboxStatus Status { get; set; } = IntegrationOutboxStatus.Pending;

    [Required]
    public string Payload { get; set; } = string.Empty;

    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? LockedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    [MaxLength(64)]
    public string? CorrelationId { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
