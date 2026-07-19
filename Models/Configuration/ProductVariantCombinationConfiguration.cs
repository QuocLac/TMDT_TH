using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WebApplication2.Models.Configuration;

public sealed class ProductVariantCombinationConfiguration
    : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> entity)
    {
        entity.Property(item => item.OptionCombinationKey)
            .HasMaxLength(64)
            .IsUnicode(false);

        entity.HasIndex(item => new
        {
            item.ProductId,
            item.OptionCombinationKey
        })
        .IsUnique()
        .HasFilter("[OptionCombinationKey] IS NOT NULL");
    }
}
