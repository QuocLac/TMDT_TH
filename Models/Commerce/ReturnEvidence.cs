using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

[Index(nameof(ReturnRequestId), nameof(CreatedAt))]
public sealed class ReturnEvidence
{
    public long Id { get; set; }

    public long ReturnRequestId { get; set; }
    public ReturnRequest ReturnRequest { get; set; } = null!;

    public ReturnEvidenceType Type { get; set; }

    [Required, MaxLength(500), Url]
    public string Url { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Caption { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
