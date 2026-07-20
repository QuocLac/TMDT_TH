using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Configuration;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

[EntityTypeConfiguration(typeof(ProductReviewConfiguration))]
public sealed class ProductReview : BaseEntity
{
    public long Id { get; set; }

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public int OrderItemId { get; set; }

    public OrderItem OrderItem { get; set; } = null!;

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    [Range(1, 5)]
    public byte Rating { get; set; }

    [MaxLength(120)]
    public string? Title { get; set; }

    [Required, MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    public ProductReviewStatus Status { get; set; } = ProductReviewStatus.Pending;

    public bool IsVerifiedPurchase { get; set; } = true;

    public DateTime SubmittedAt { get; set; }

    public DateTime? EditedAt { get; set; }

    public DateTime? PublishedAt { get; set; }

    public DateTime? ModeratedAt { get; set; }

    [MaxLength(100)]
    public string? ModeratedBy { get; set; }

    [MaxLength(500)]
    public string? ModerationNote { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public ICollection<ProductReviewMedia> Media { get; set; } = [];

    public ProductReviewReply? Reply { get; set; }
}
