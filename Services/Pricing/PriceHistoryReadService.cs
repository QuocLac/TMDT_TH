using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Pricing;

public interface IPriceHistoryReadService
{
    Task<VariantPriceLedgerSummary?> GetVariantSummaryAsync(
        int variantId,
        DateTime asOfUtc,
        int lookbackDays,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<VariantPriceLedgerEntry>>
        GetTimelineAsync(
            int variantId,
            DateTime fromUtc,
            DateTime toUtc,
            PriceHistoryKind? priceKind,
            CancellationToken cancellationToken);
}

public sealed record VariantPriceLedgerSummary(
    int ProductId,
    string ProductName,
    int VariantId,
    string Sku,
    string Currency,
    decimal ListPrice,
    decimal CurrentPrice,
    decimal LowestEffectivePrice,
    decimal HighestEffectivePrice,
    int EffectivePriceChangeCount,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    EffectivePriceSourceType CurrentSourceType,
    int? CurrentSourceId);

public sealed record VariantPriceLedgerEntry(
    int Id,
    PriceHistoryKind PriceKind,
    string Currency,
    decimal OldPrice,
    decimal NewPrice,
    PriceHistoryEventType EventType,
    PriceChangeSourceType SourceType,
    int? SourceId,
    string? CorrelationId,
    string? Reason,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    DateTime RecordedAtUtc,
    string ChangedBy,
    string Note);

/// <summary>
/// Truy vấn sổ giá theo biến thể và tính mức giá bán hiệu lực
/// thấp nhất/cao nhất trong cửa sổ thời gian.
/// </summary>
public sealed class PriceHistoryReadService
    : IPriceHistoryReadService
{
    private const int MaximumLookbackDays = 366;

    private readonly ApplicationDbContext _context;

    public PriceHistoryReadService(
        ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<VariantPriceLedgerSummary?>
        GetVariantSummaryAsync(
            int variantId,
            DateTime asOfUtc,
            int lookbackDays,
            CancellationToken cancellationToken)
    {
        if (variantId <= 0)
        {
            return null;
        }

        lookbackDays = Math.Clamp(
            lookbackDays,
            1,
            MaximumLookbackDays);
        asOfUtc = NormalizeUtc(asOfUtc);
        var windowStartUtc = asOfUtc.AddDays(-lookbackDays);

        var variant = await _context.ProductVariants
            .AsNoTracking()
            .Where(item => item.Id == variantId)
            .Select(item => new
            {
                item.Id,
                item.ProductId,
                ProductName = item.Product.Name,
                item.SKU,
                item.Price,
                item.CurrentPrice,
                item.CurrentPriceSourceType,
                item.CurrentPriceSourceId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (variant is null)
        {
            return null;
        }

        var effectiveHistory = _context.PriceHistories
            .AsNoTracking()
            .Where(item =>
                item.ProductVariantId == variantId
                && item.PriceKind
                    == PriceHistoryKind.EffectivePrice
                && (item.EffectiveFrom ?? item.CreatedAt)
                    < asOfUtc);

        var eventBeforeWindow = await effectiveHistory
            .Where(item =>
                (item.EffectiveFrom ?? item.CreatedAt)
                    < windowStartUtc)
            .OrderByDescending(item =>
                item.EffectiveFrom ?? item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Select(item => new
            {
                item.NewPrice,
                item.Currency
            })
            .FirstOrDefaultAsync(cancellationToken);

        var eventsInWindow = await effectiveHistory
            .Where(item =>
                (item.EffectiveFrom ?? item.CreatedAt)
                    >= windowStartUtc)
            .OrderBy(item =>
                item.EffectiveFrom ?? item.CreatedAt)
            .ThenBy(item => item.Id)
            .Select(item => new
            {
                item.OldPrice,
                item.NewPrice,
                item.Currency
            })
            .ToListAsync(cancellationToken);

        var observedPrices = new List<decimal>(
            eventsInWindow.Count + 1);

        if (eventBeforeWindow is not null)
        {
            observedPrices.Add(eventBeforeWindow.NewPrice);
        }
        else if (eventsInWindow.Count > 0)
        {
            observedPrices.Add(eventsInWindow[0].OldPrice);
        }
        else
        {
            observedPrices.Add(variant.CurrentPrice);
        }

        observedPrices.AddRange(
            eventsInWindow.Select(item => item.NewPrice));

        var currency = eventsInWindow
            .LastOrDefault()
            ?.Currency
            ?? eventBeforeWindow?.Currency
            ?? "VND";

        return new VariantPriceLedgerSummary(
            variant.ProductId,
            variant.ProductName,
            variant.Id,
            variant.SKU,
            currency,
            variant.Price,
            variant.CurrentPrice,
            observedPrices.Min(),
            observedPrices.Max(),
            eventsInWindow.Count,
            windowStartUtc,
            asOfUtc,
            variant.CurrentPriceSourceType,
            variant.CurrentPriceSourceId);
    }

    public async Task<IReadOnlyList<VariantPriceLedgerEntry>>
        GetTimelineAsync(
            int variantId,
            DateTime fromUtc,
            DateTime toUtc,
            PriceHistoryKind? priceKind,
            CancellationToken cancellationToken)
    {
        if (variantId <= 0)
        {
            return [];
        }

        fromUtc = NormalizeUtc(fromUtc);
        toUtc = NormalizeUtc(toUtc);

        if (toUtc <= fromUtc)
        {
            return [];
        }

        var query = _context.PriceHistories
            .AsNoTracking()
            .Where(item =>
                item.ProductVariantId == variantId
                && (item.EffectiveFrom ?? item.CreatedAt)
                    >= fromUtc
                && (item.EffectiveFrom ?? item.CreatedAt)
                    < toUtc);

        if (priceKind.HasValue)
        {
            query = query.Where(item =>
                item.PriceKind == priceKind.Value);
        }

        return await query
            .OrderByDescending(item =>
                item.EffectiveFrom ?? item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Select(item => new VariantPriceLedgerEntry(
                item.Id,
                item.PriceKind,
                item.Currency,
                item.OldPrice,
                item.NewPrice,
                item.EventType,
                item.SourceType,
                item.SourceId,
                item.CorrelationId,
                item.Reason,
                item.EffectiveFrom ?? item.CreatedAt,
                item.EffectiveTo,
                item.CreatedAt,
                item.ChangedBy,
                item.Note))
            .ToListAsync(cancellationToken);
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(
                value,
                DateTimeKind.Utc)
        };
    }
}
