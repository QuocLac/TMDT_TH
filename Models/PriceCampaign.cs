using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

public sealed class PriceCampaign : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public PriceCampaignMode Mode { get; set; } = PriceCampaignMode.FixedWindow;

    public PriceCampaignStatus Status { get; set; } = PriceCampaignStatus.Draft;

    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public PriceChangeSourceType SourceType { get; set; } = PriceChangeSourceType.Manual;

    public PriceConflictPolicy ConflictPolicy { get; set; } = PriceConflictPolicy.Reject;

    [MaxLength(64)]
    public string? ClientRequestId { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    [MaxLength(100)]
    public string? ConfirmedBy { get; set; }

    public DateTime? CancelledAt { get; set; }

    [MaxLength(100)]
    public string? CancelledBy { get; set; }

    public int? SupersededByCampaignId { get; set; }

    public PriceCampaign? SupersededByCampaign { get; set; }

    public ICollection<PriceCampaign> SupersededCampaigns { get; set; } = [];

    [Required, MaxLength(100)]
    public string CreatedBy { get; set; } = string.Empty;

    // Compatibility flag for the old UI. Status + effective time are authoritative.
    public bool IsActive { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public ICollection<PriceCampaignItem> CampaignItems { get; set; } = [];
}
