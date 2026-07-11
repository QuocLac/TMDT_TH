using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

[Table("ReturnInspections")]
[Index(nameof(IdempotencyKey), IsUnique = true)]
[Index(nameof(ReturnRequestId), nameof(CreatedAt))]
public sealed class ReturnInspection
{
    public long Id { get; set; }

    public long ReturnRequestId { get; set; }
    public ReturnRequest ReturnRequest { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Inspector { get; set; } = string.Empty;

    public ReturnInspectionResult Result { get; set; }

    [Required, MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string PayloadHash { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
