using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Tests.Support;

namespace WebApplication2.Tests.Pricing;

public sealed class PriceHistoryModelConfigurationTests
{
    [Fact]
    public void Price_history_has_canonical_ledger_index()
    {
        using var context =
            TestDbContextFactory.CreateSqlServerModelContext();

        var entityType = context.Model.FindEntityType(
            typeof(PriceHistory));

        Assert.NotNull(entityType);

        var index = entityType!.GetIndexes()
            .Single(candidate =>
                candidate.Properties
                    .Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(PriceHistory.ProductVariantId),
                        nameof(PriceHistory.PriceKind),
                        nameof(PriceHistory.EffectiveFrom),
                        nameof(PriceHistory.CreatedAt)
                    ]));

        Assert.False(index.IsUnique);
    }

    [Fact]
    public void Price_history_uses_restrict_delete_behavior()
    {
        using var context =
            TestDbContextFactory.CreateSqlServerModelContext();

        var entityType = context.Model.FindEntityType(
            typeof(PriceHistory));

        var foreignKey = Assert.Single(
            entityType!.GetForeignKeys());

        Assert.Equal(
            DeleteBehavior.Restrict,
            foreignKey.DeleteBehavior);
    }

    [Fact]
    public void Price_history_defaults_are_canonical()
    {
        var history = new PriceHistory();

        Assert.Equal(
            PriceHistoryKind.EffectivePrice,
            history.PriceKind);
        Assert.Equal("VND", history.Currency);
    }
}
