using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Configuration;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

[EntityTypeConfiguration(typeof(ProductReviewMessageConfiguration))]
public sealed class ProductReviewMessage : BaseEntity
{
    public long Id { get; set; }

    public long ProductReviewId { get; set; }

    public ProductReview ProductReview { get; set; } = null!;

    public ProductReviewMessageAuthorType AuthorType { get; set; }

    public int? CustomerId { get; set; }

    public Customer? Customer { get; set; }

    [Required, MaxLength(100)]
    public string AuthorName { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string Content { get; set; } = string.Empty;

    public DateTime SentAt { get; set; }
}
