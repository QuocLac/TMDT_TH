using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models.Configuration;

public sealed class ProductReviewConfiguration
    : IEntityTypeConfiguration<ProductReview>
{
    public void Configure(EntityTypeBuilder<ProductReview> entity)
    {
        entity.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(ProductReviewStatus.Published);

        entity.Property(item => item.IsVerifiedPurchase)
            .HasDefaultValue(true)
            .HasSentinel(true);

        entity.Property(item => item.RowVersion)
            .IsRowVersion();

        entity.HasIndex(item => item.OrderItemId)
            .IsUnique();

        entity.HasIndex(item => new
        {
            item.ProductId,
            item.Status,
            item.SubmittedAt
        });

        entity.HasIndex(item => new
        {
            item.CustomerId,
            item.SubmittedAt
        });

        entity.HasOne(item => item.Product)
            .WithMany(item => item.Reviews)
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(item => item.OrderItem)
            .WithOne(item => item.Review)
            .HasForeignKey<ProductReview>(item => item.OrderItemId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(item => item.Customer)
            .WithMany(item => item.Reviews)
            .HasForeignKey(item => item.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.ToTable("ProductReviews", table =>
        {
            table.HasCheckConstraint(
                "CK_ProductReview_Rating",
                "[Rating] >= 1 AND [Rating] <= 5");
        });
    }
}

public sealed class ProductReviewMediaConfiguration
    : IEntityTypeConfiguration<ProductReviewMedia>
{
    public void Configure(EntityTypeBuilder<ProductReviewMedia> entity)
    {
        entity.Property(item => item.Type)
            .HasConversion<string>()
            .HasMaxLength(20);

        entity.HasIndex(item => new
        {
            item.ProductReviewId,
            item.DisplayOrder
        })
        .IsUnique();

        entity.HasOne(item => item.ProductReview)
            .WithMany(item => item.Media)
            .HasForeignKey(item => item.ProductReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.ToTable("ProductReviewMedia", table =>
        {
            table.HasCheckConstraint(
                "CK_ProductReviewMedia_DisplayOrder",
                "[DisplayOrder] >= 0 AND [DisplayOrder] <= 4");
        });
    }
}

public sealed class ProductReviewReplyConfiguration
    : IEntityTypeConfiguration<ProductReviewReply>
{
    public void Configure(EntityTypeBuilder<ProductReviewReply> entity)
    {
        entity.HasIndex(item => item.ProductReviewId)
            .IsUnique();

        entity.HasOne(item => item.ProductReview)
            .WithOne(item => item.Reply)
            .HasForeignKey<ProductReviewReply>(item => item.ProductReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.ToTable("ProductReviewReplies");
    }
}
