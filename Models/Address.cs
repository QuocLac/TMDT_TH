using System.ComponentModel.DataAnnotations;

namespace WebApplication2.Models;

public class Address : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    [MaxLength(100)]
    public string? RecipientName { get; set; }

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    [Required, MaxLength(255)]
    public string Street { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Ward { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string District { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string City { get; set; } = string.Empty;

    public int? ProvinceId { get; set; }
    public int? DistrictId { get; set; }

    [MaxLength(30)]
    public string? WardCode { get; set; }

    public DateTime? ValidatedAt { get; set; }
    public bool IsDefault { get; set; }
}
