using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WebApplication2.Models.Configuration;

public sealed class ProductReviewMessageConfiguration
    : IEntityTypeConfiguration<ProductReviewMessage>
{
    public void Configure(EntityTypeBuilder<ProductReviewMessage> entity)
    {
        entity.Property(item => item.AuthorType)
            .HasConversion<string>()
            .HasMaxLength(20);

        entity.HasIndex(item => new
        {
            item.ProductReviewId,
            item.SentAt,
            item.Id
        });

        entity.HasOne(item => item.ProductReview)
            .WithMany(item => item.Messages)
            .HasForeignKey(item => item.ProductReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(item => item.Customer)
            .WithMany()
            .HasForeignKey(item => item.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.ToTable("ProductReviewMessages");
    }
}
