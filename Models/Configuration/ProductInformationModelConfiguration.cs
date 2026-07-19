using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models.Configuration;

public sealed class ProductAttributeDefinitionConfiguration
    : IEntityTypeConfiguration<ProductAttributeDefinition>
{
    public void Configure(EntityTypeBuilder<ProductAttributeDefinition> entity)
    {
        entity.Property(item => item.DataType)
            .HasConversion<string>()
            .HasMaxLength(30);

        entity.Property(item => item.IsCustomerVisible)
            .HasDefaultValue(true)
            .HasSentinel(true);

        entity.Property(item => item.IsActive)
            .HasDefaultValue(true)
            .HasSentinel(true);

        entity.Property(item => item.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        entity.HasIndex(item => new
        {
            item.IsActive,
            item.IsCustomerVisible
        });

        entity.ToTable("ProductAttributeDefinitions", table =>
        {
            table.HasCheckConstraint(
                "CK_ProductAttributeDefinition_DataType",
                "[DataType] IN ('ShortText','LongText','Number','Boolean','Date','SingleChoice')");
        });
    }
}

public sealed class ProductAttributeOptionConfiguration
    : IEntityTypeConfiguration<ProductAttributeOption>
{
    public void Configure(EntityTypeBuilder<ProductAttributeOption> entity)
    {
        entity.Property(item => item.IsActive)
            .HasDefaultValue(true)
            .HasSentinel(true);

        entity.Property(item => item.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        entity.HasIndex(item => new
        {
            item.AttributeDefinitionId,
            item.DisplayOrder
        });

        entity.HasAlternateKey(item => new
        {
            item.AttributeDefinitionId,
            item.Id
        });

        entity.HasOne(item => item.AttributeDefinition)
            .WithMany(item => item.Options)
            .HasForeignKey(item => item.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.ToTable("ProductAttributeOptions", table =>
        {
            table.HasCheckConstraint(
                "CK_ProductAttributeOption_DisplayOrder",
                "[DisplayOrder] >= 0 AND [DisplayOrder] <= 9999");
        });
    }
}

public sealed class CategoryProductAttributeConfiguration
    : IEntityTypeConfiguration<CategoryProductAttribute>
{
    public void Configure(EntityTypeBuilder<CategoryProductAttribute> entity)
    {
        entity.HasIndex(item => new
        {
            item.CategoryId,
            item.DisplayOrder
        });

        entity.HasOne(item => item.Category)
            .WithMany(item => item.ProductAttributeAssignments)
            .HasForeignKey(item => item.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(item => item.AttributeDefinition)
            .WithMany(item => item.CategoryAssignments)
            .HasForeignKey(item => item.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.ToTable("CategoryProductAttributes", table =>
        {
            table.HasCheckConstraint(
                "CK_CategoryProductAttribute_DisplayOrder",
                "[DisplayOrder] >= 0 AND [DisplayOrder] <= 9999");
        });
    }
}

public sealed class ProductAttributeValueConfiguration
    : IEntityTypeConfiguration<ProductAttributeValue>
{
    public void Configure(EntityTypeBuilder<ProductAttributeValue> entity)
    {
        entity.Property(item => item.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        entity.Property(item => item.DateValue)
            .HasColumnType("date");

        entity.Property(item => item.RowVersion)
            .IsRowVersion();

        entity.HasOne(item => item.Product)
            .WithMany(item => item.AttributeValues)
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne(item => item.AttributeDefinition)
            .WithMany(item => item.ProductValues)
            .HasForeignKey(item => item.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(item => item.Option)
            .WithMany(item => item.ProductValues)
            .HasForeignKey(item => new
            {
                item.AttributeDefinitionId,
                item.OptionId
            })
            .HasPrincipalKey(item => new
            {
                item.AttributeDefinitionId,
                item.Id
            })
            .OnDelete(DeleteBehavior.Restrict);

        entity.ToTable("ProductAttributeValues", table =>
        {
            table.HasCheckConstraint(
                "CK_ProductAttributeValue_OneValue",
                "(CASE WHEN [TextValue] IS NULL THEN 0 ELSE 1 END) + " +
                "(CASE WHEN [NumberValue] IS NULL THEN 0 ELSE 1 END) + " +
                "(CASE WHEN [BooleanValue] IS NULL THEN 0 ELSE 1 END) + " +
                "(CASE WHEN [DateValue] IS NULL THEN 0 ELSE 1 END) + " +
                "(CASE WHEN [OptionId] IS NULL THEN 0 ELSE 1 END) = 1");
        });
    }
}
