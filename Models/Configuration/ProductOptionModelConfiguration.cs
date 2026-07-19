using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WebApplication2.Models.Configuration;

public sealed class ProductOptionGroupConfiguration
    : IEntityTypeConfiguration<ProductOptionGroup>
{
    public void Configure(EntityTypeBuilder<ProductOptionGroup> entity)
    {
        entity.Property(item => item.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        entity.Property(item => item.IsRequired)
            .HasDefaultValue(true)
            .HasSentinel(true);

        entity.Property(item => item.IsActive)
            .HasDefaultValue(true)
            .HasSentinel(true);

        entity.HasIndex(item => new
        {
            item.ProductId,
            item.Code
        })
        .IsUnique();

        entity.HasIndex(item => new
        {
            item.ProductId,
            item.IsActive,
            item.DisplayOrder
        });

        entity.HasOne(item => item.Product)
            .WithMany(item => item.OptionGroups)
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.ToTable("ProductOptionGroups", table =>
        {
            table.HasCheckConstraint(
                "CK_ProductOptionGroup_DisplayOrder",
                "[DisplayOrder] >= 0 AND [DisplayOrder] <= 9999");
        });
    }
}

public sealed class ProductOptionValueConfiguration
    : IEntityTypeConfiguration<ProductOptionValue>
{
    public void Configure(EntityTypeBuilder<ProductOptionValue> entity)
    {
        entity.Property(item => item.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        entity.Property(item => item.IsActive)
            .HasDefaultValue(true)
            .HasSentinel(true);

        entity.HasAlternateKey(item => new
        {
            item.OptionGroupId,
            item.Id
        });

        entity.HasIndex(item => new
        {
            item.OptionGroupId,
            item.Code
        })
        .IsUnique();

        entity.HasIndex(item => new
        {
            item.OptionGroupId,
            item.IsActive,
            item.DisplayOrder
        });

        entity.HasOne(item => item.OptionGroup)
            .WithMany(item => item.Values)
            .HasForeignKey(item => item.OptionGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.ToTable("ProductOptionValues", table =>
        {
            table.HasCheckConstraint(
                "CK_ProductOptionValue_DisplayOrder",
                "[DisplayOrder] >= 0 AND [DisplayOrder] <= 9999");
        });
    }
}

public sealed class ProductVariantOptionSelectionConfiguration
    : IEntityTypeConfiguration<ProductVariantOptionSelection>
{
    public void Configure(EntityTypeBuilder<ProductVariantOptionSelection> entity)
    {
        entity.HasKey(item => new
        {
            item.VariantId,
            item.OptionGroupId
        });

        entity.HasIndex(item => new
        {
            item.OptionGroupId,
            item.OptionValueId
        });

        entity.HasOne(item => item.Variant)
            .WithMany(item => item.OptionSelections)
            .HasForeignKey(item => item.VariantId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(item => item.OptionGroup)
            .WithMany(item => item.VariantSelections)
            .HasForeignKey(item => item.OptionGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(item => item.OptionValue)
            .WithMany(item => item.VariantSelections)
            .HasForeignKey(item => new
            {
                item.OptionGroupId,
                item.OptionValueId
            })
            .HasPrincipalKey(item => new
            {
                item.OptionGroupId,
                item.Id
            })
            .OnDelete(DeleteBehavior.Restrict);

        entity.ToTable("ProductVariantOptionSelections");
    }
}
