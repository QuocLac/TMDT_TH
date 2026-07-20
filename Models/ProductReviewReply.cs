using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Configuration;

namespace WebApplication2.Models;

[EntityTypeConfiguration(typeof(ProductReviewReplyConfiguration))]
public sealed class ProductReviewReply : BaseEntity
{
    public long Id { get; set; }

    public long ProductReviewId { get; set; }

    public ProductReview ProductReview { get; set; } = null!;

    [Required, MaxLength(1000)]
    public string Content { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string RepliedBy { get; set; } = string.Empty;

    public DateTime RepliedAt { get; set; }
}
