using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

public class Account : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? NormalizedUsername { get; set; }

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? NormalizedEmail { get; set; }

    [Required, MaxLength(500)]
    public string Avt { get; set; } = "/images/default-avatar.png";

    public AccountRole Role { get; set; } = AccountRole.Customer;

    [MaxLength(64)]
    public string? SecurityStamp { get; set; }

    public DateTime? LastLoginAt { get; set; }
    public int FailedAccessCount { get; set; }
    public DateTime? LockoutEndAt { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public bool IsActive { get; set; } = true;

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public Customer? Customer { get; set; }
}
