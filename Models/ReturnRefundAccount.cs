using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Configuration;

namespace WebApplication2.Models;

[EntityTypeConfiguration(typeof(ReturnRefundAccountConfiguration))]
public sealed class ReturnRefundAccount : BaseEntity
{
    public long Id { get; set; }

    public long ReturnRequestId { get; set; }

    public ReturnRequest ReturnRequest { get; set; } = null!;

    [Required, MaxLength(12)]
    public string BankBin { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string BankCode { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string BankName { get; set; } = string.Empty;

    [Required, MaxLength(24)]
    public string AccountNumber { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string AccountName { get; set; } = string.Empty;

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
