using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

public sealed class IntegrationInboxEvent : BaseEntity
{
    public long Id { get; set; }

    [Required, MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string EventType { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? ExternalEventId { get; set; }

    [Required, MaxLength(160)]
    public string DeduplicationKey { get; set; } = string.Empty;

    public IntegrationEventStatus Status { get; set; } = IntegrationEventStatus.Received;

    [Required]
    public string Payload { get; set; } = string.Empty;

    public string? Headers { get; set; }
    public DateTime? ProviderOccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int AttemptCount { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    [MaxLength(64)]
    public string? CorrelationId { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
