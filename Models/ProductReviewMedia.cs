using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Configuration;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

[EntityTypeConfiguration(typeof(ProductReviewMediaConfiguration))]
public sealed class ProductReviewMedia : BaseEntity
{
    public long Id { get; set; }

    public long ProductReviewId { get; set; }

    public ProductReview ProductReview { get; set; } = null!;

    public ProductReviewMediaType Type { get; set; }

    [Required, MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    [Range(0, 4)]
    public int DisplayOrder { get; set; }
}
