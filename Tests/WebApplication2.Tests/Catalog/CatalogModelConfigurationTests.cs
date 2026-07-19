using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using WebApplication2.Models;
using WebApplication2.Tests.Support;

namespace WebApplication2.Tests.Catalog;

public sealed class CatalogModelConfigurationTests
{
    [Fact]
    public void Product_option_combination_index_is_unique_and_filtered()
    {
        using var context =
            TestDbContextFactory.CreateSqlServerModelContext();

        var entityType = context.Model.FindEntityType(
            typeof(ProductVariant));

        Assert.NotNull(entityType);

        var index = entityType!.GetIndexes()
            .Single(candidate =>
                candidate.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(ProductVariant.ProductId),
                        nameof(ProductVariant.OptionCombinationKey)
                    ]));

        Assert.True(index.IsUnique);
        Assert.Contains(
            nameof(ProductVariant.OptionCombinationKey),
            index.GetFilter() ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(ProductAttributeDefinition), "ProductAttributeDefinitions")]
    [InlineData(typeof(ProductAttributeOption), "ProductAttributeOptions")]
    [InlineData(typeof(CategoryProductAttribute), "CategoryProductAttributes")]
    [InlineData(typeof(ProductAttributeValue), "ProductAttributeValues")]
    [InlineData(typeof(ProductOptionGroup), "ProductOptionGroups")]
    [InlineData(typeof(ProductOptionValue), "ProductOptionValues")]
    [InlineData(typeof(ProductVariantOptionSelection), "ProductVariantOptionSelections")]
    public void Catalog_architecture_uses_canonical_table_names(
        Type entityClrType,
        string expectedTableName)
    {
        using var context =
            TestDbContextFactory.CreateSqlServerModelContext();

        var entityType = context.Model.FindEntityType(entityClrType);

        Assert.NotNull(entityType);
        Assert.Equal(expectedTableName, entityType!.GetTableName());
    }
}
