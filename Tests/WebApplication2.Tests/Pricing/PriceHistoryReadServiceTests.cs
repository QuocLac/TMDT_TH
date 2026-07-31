using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Pricing;
using WebApplication2.Tests.Support;

namespace WebApplication2.Tests.Pricing;

public sealed class PriceHistoryReadServiceTests
{
    [Fact]
    public async Task Summary_uses_only_effective_price_events()
    {
        await using var context =
            TestDbContextFactory.Create();

        var category =
            CatalogTestData.CreateCategory("price-ledger");
        var product = CatalogTestData.CreateValidProduct(
            category,
            "price-ledger",
            isActive: true);
        var variant = product.Variants.Single();

        variant.Price = 160_000m;
        variant.CurrentPrice = 90_000m;

        context.Products.Add(product);
        await context.SaveChangesAsync();

        var asOfUtc = new DateTime(
            2026,
            7,
            31,
            12,
            0,
            0,
            DateTimeKind.Utc);

        context.PriceHistories.AddRange(
            CreateEffective(
                variant.Id,
                130_000m,
                120_000m,
                asOfUtc.AddDays(-40)),
            CreateEffective(
                variant.Id,
                120_000m,
                100_000m,
                asOfUtc.AddDays(-20)),
            CreateEffective(
                variant.Id,
                100_000m,
                90_000m,
                asOfUtc.AddDays(-5)),
            new PriceHistory
            {
                ProductVariantId = variant.Id,
                PriceKind = PriceHistoryKind.ListPrice,
                Currency = "VND",
                OldPrice = 150_000m,
                NewPrice = 160_000m,
                EventType =
                    PriceHistoryEventType.ListPriceChanged,
                SourceType = PriceChangeSourceType.Manual,
                EffectiveFrom = asOfUtc.AddDays(-3),
                CreatedAt = asOfUtc.AddDays(-3),
                ChangedBy = "Admin",
                Note = "Đổi giá niêm yết"
            });

        await context.SaveChangesAsync();

        var service = new PriceHistoryReadService(context);
        var summary = await service.GetVariantSummaryAsync(
            variant.Id,
            asOfUtc,
            30,
            CancellationToken.None);

        Assert.NotNull(summary);
        Assert.Equal(90_000m, summary!.LowestEffectivePrice);
        Assert.Equal(120_000m, summary.HighestEffectivePrice);
        Assert.Equal(2, summary.EffectivePriceChangeCount);
        Assert.Equal(160_000m, summary.ListPrice);
        Assert.Equal(90_000m, summary.CurrentPrice);
    }

    [Fact]
    public async Task Timeline_can_filter_price_kind()
    {
        await using var context =
            TestDbContextFactory.Create();

        var category =
            CatalogTestData.CreateCategory("timeline");
        var product = CatalogTestData.CreateValidProduct(
            category,
            "timeline",
            isActive: true);
        var variant = product.Variants.Single();

        context.Products.Add(product);
        await context.SaveChangesAsync();

        var nowUtc = new DateTime(
            2026,
            7,
            31,
            12,
            0,
            0,
            DateTimeKind.Utc);

        context.PriceHistories.AddRange(
            CreateEffective(
                variant.Id,
                250_000m,
                220_000m,
                nowUtc.AddDays(-2)),
            new PriceHistory
            {
                ProductVariantId = variant.Id,
                PriceKind = PriceHistoryKind.ListPrice,
                Currency = "VND",
                OldPrice = 250_000m,
                NewPrice = 270_000m,
                EventType =
                    PriceHistoryEventType.ListPriceChanged,
                SourceType = PriceChangeSourceType.Manual,
                EffectiveFrom = nowUtc.AddDays(-1),
                CreatedAt = nowUtc.AddDays(-1),
                ChangedBy = "Admin",
                Note = "Đổi giá niêm yết"
            });

        await context.SaveChangesAsync();

        var service = new PriceHistoryReadService(context);
        var timeline = await service.GetTimelineAsync(
            variant.Id,
            nowUtc.AddDays(-7),
            nowUtc,
            PriceHistoryKind.ListPrice,
            CancellationToken.None);

        var entry = Assert.Single(timeline);
        Assert.Equal(
            PriceHistoryKind.ListPrice,
            entry.PriceKind);
        Assert.Equal(270_000m, entry.NewPrice);
    }

    private static PriceHistory CreateEffective(
        int variantId,
        decimal oldPrice,
        decimal newPrice,
        DateTime effectiveFromUtc)
    {
        return new PriceHistory
        {
            ProductVariantId = variantId,
            PriceKind = PriceHistoryKind.EffectivePrice,
            Currency = "VND",
            OldPrice = oldPrice,
            NewPrice = newPrice,
            EventType =
                PriceHistoryEventType.EffectivePriceChanged,
            SourceType = PriceChangeSourceType.Manual,
            EffectiveFrom = effectiveFromUtc,
            CreatedAt = effectiveFromUtc,
            ChangedBy = "Admin",
            Note = "Đổi giá bán hiệu lực"
        };
    }
}
