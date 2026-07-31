using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Pricing;

namespace WebApplication2.Tests.Pricing;

public sealed class PriceHistoryAppendOnlyInterceptorTests
{
    [Fact]
    public async Task Added_history_is_normalized()
    {
        await using var context = CreateContext();

        var history = CreateHistory();
        history.Currency = "vnd";
        history.EffectiveFrom = null;

        context.PriceHistories.Add(history);
        await context.SaveChangesAsync();

        Assert.Equal("VND", history.Currency);
        Assert.NotNull(history.EffectiveFrom);
        Assert.Null(history.UpdatedAt);
    }

    [Fact]
    public async Task Existing_history_cannot_be_modified()
    {
        await using var context = CreateContext();

        var history = CreateHistory();
        context.PriceHistories.Add(history);
        await context.SaveChangesAsync();

        history.Note = "Không được phép sửa";

        var exception = await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => context.SaveChangesAsync());

        Assert.Contains(
            "sổ bất biến",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Existing_history_cannot_be_deleted()
    {
        await using var context = CreateContext();

        var history = CreateHistory();
        context.PriceHistories.Add(history);
        await context.SaveChangesAsync();

        context.PriceHistories.Remove(history);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveChangesAsync());
    }

    private static ApplicationDbContext CreateContext()
    {
        var options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(
                    $"price-ledger-{Guid.NewGuid():N}")
                .AddInterceptors(
                    new PriceHistoryAppendOnlyInterceptor(
                        TimeProvider.System))
                .Options;

        return new ApplicationDbContext(options);
    }

    private static PriceHistory CreateHistory()
    {
        return new PriceHistory
        {
            ProductVariantId = 1,
            PriceKind = PriceHistoryKind.EffectivePrice,
            Currency = "VND",
            OldPrice = 120_000m,
            NewPrice = 100_000m,
            EventType =
                PriceHistoryEventType.EffectivePriceChanged,
            SourceType = PriceChangeSourceType.Manual,
            EffectiveFrom = DateTime.UtcNow,
            ChangedBy = "Admin",
            Note = "Kiểm thử ledger"
        };
    }
}
