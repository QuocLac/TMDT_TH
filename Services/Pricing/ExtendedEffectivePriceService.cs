using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Pricing;

/// <summary>
/// Extends the stable pricing pipeline with explicit increase operations.
/// The underlying service still owns conflict validation and projection writes.
/// Increase inputs are converted to variant-specific fixed prices for calculation,
/// then mapped back to their original semantic type for persistence and audit.
/// </summary>
public sealed class ExtendedEffectivePriceService : IEffectivePriceService
{
    private readonly ReliableEffectivePriceService _inner;
    private readonly ApplicationDbContext _context;

    public ExtendedEffectivePriceService(
        ReliableEffectivePriceService inner,
        ApplicationDbContext context)
    {
        _inner = inner;
        _context = context;
    }

    public Task<CampaignPricingValidationResult> ValidateCampaignAsync(
        int campaignId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        IReadOnlyCollection<CampaignPriceInput> items,
        CancellationToken cancellationToken)
    {
        return _inner.ValidateCampaignAsync(
            campaignId,
            startDateUtc,
            endDateUtc,
            items,
            cancellationToken);
    }

    public Task<CampaignPricingValidationResult> ValidateCampaignAsync(
        int campaignId,
        PriceCampaignMode mode,
        DateTime startDateUtc,
        DateTime? endDateUtc,
        PriceConflictPolicy conflictPolicy,
        IReadOnlyCollection<CampaignPriceInput> items,
        CancellationToken cancellationToken)
    {
        return _inner.ValidateCampaignAsync(
            campaignId,
            mode,
            startDateUtc,
            endDateUtc,
            conflictPolicy,
            items,
            cancellationToken);
    }

    public async Task<PricePlanPreviewResult> PreviewCampaignAsync(
        int campaignId,
        PriceCampaignMode mode,
        DateTime startDateUtc,
        DateTime? endDateUtc,
        PriceConflictPolicy conflictPolicy,
        IReadOnlyCollection<PricePlanPreviewInput> items,
        CancellationToken cancellationToken)
    {
        var increaseInputs = items
            .Where(item => IsIncrease(item.AdjustmentType))
            .ToArray();

        if (increaseInputs.Length == 0)
        {
            return await _inner.PreviewCampaignAsync(
                campaignId,
                mode,
                startDateUtc,
                endDateUtc,
                conflictPolicy,
                items,
                cancellationToken);
        }

        var increaseVariantIds = increaseInputs
            .Select(item => item.VariantId)
            .Distinct()
            .ToArray();

        var listPrices = await _context.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                increaseVariantIds.Contains(variant.Id))
            .Select(variant => new
            {
                variant.Id,
                variant.SKU,
                variant.Price
            })
            .ToDictionaryAsync(
                item => item.Id,
                cancellationToken);

        var missingIds = increaseVariantIds
            .Where(id => !listPrices.ContainsKey(id))
            .OrderBy(id => id)
            .ToArray();

        if (missingIds.Length > 0)
        {
            return PricePlanPreviewResult.Failure(
                $"Không tìm thấy biến thể ID: {string.Join(", ", missingIds)}.",
                "VARIANT_NOT_FOUND");
        }

        var originalByVariantId = increaseInputs
            .ToDictionary(item => item.VariantId);

        PricePlanPreviewInput[] normalizedInputs;

        try
        {
            normalizedInputs = items
                .Select(item =>
                {
                    if (!IsIncrease(item.AdjustmentType))
                    {
                        return item;
                    }

                    var variant = listPrices[item.VariantId];
                    var fixedPrice =
                        PriceIncreaseCalculator.Calculate(
                            variant.Price,
                            item.AdjustmentType,
                            item.AdjustmentValue,
                            variant.SKU);

                    return item with
                    {
                        AdjustmentType =
                            PriceAdjustmentType.FixedPrice,
                        AdjustmentValue = fixedPrice
                    };
                })
                .ToArray();
        }
        catch (InvalidPriceIncreaseException exception)
        {
            return PricePlanPreviewResult.Failure(
                exception.Message,
                "INVALID_ADJUSTMENT");
        }

        var preview = await _inner.PreviewCampaignAsync(
            campaignId,
            mode,
            startDateUtc,
            endDateUtc,
            conflictPolicy,
            normalizedInputs,
            cancellationToken);

        if (preview.Items.Count == 0)
        {
            return preview;
        }

        var semanticItems = preview.Items
            .Select(item =>
            {
                if (!originalByVariantId.TryGetValue(
                        item.VariantId,
                        out var original))
                {
                    return item;
                }

                return item with
                {
                    AdjustmentType =
                        original.AdjustmentType,
                    AdjustmentValue =
                        original.AdjustmentValue
                };
            })
            .ToArray();

        return preview with
        {
            Items = semanticItems
        };
    }

    public Task<EffectivePriceRecalculationResult>
        RecalculateVariantsAsync(
            IReadOnlyCollection<int> variantIds,
            string changedBy,
            string reason,
            CancellationToken cancellationToken)
    {
        return _inner.RecalculateVariantsAsync(
            variantIds,
            changedBy,
            reason,
            cancellationToken);
    }

    public Task<EffectivePriceRecalculationResult>
        RecalculateVariantsAsync(
            IReadOnlyCollection<int> variantIds,
            string changedBy,
            string reason,
            string correlationId,
            CancellationToken cancellationToken)
    {
        return _inner.RecalculateVariantsAsync(
            variantIds,
            changedBy,
            reason,
            correlationId,
            cancellationToken);
    }

    public Task<EffectivePriceRecalculationResult>
        RecalculateVariantsAsync(
            IReadOnlyCollection<int> variantIds,
            string changedBy,
            string reason,
            string correlationId,
            PriceHistoryWriteContext historyContext,
            CancellationToken cancellationToken)
    {
        return _inner.RecalculateVariantsAsync(
            variantIds,
            changedBy,
            reason,
            correlationId,
            historyContext,
            cancellationToken);
    }

    public Task<EffectivePriceRecalculationResult>
        RecalculateAffectedVariantsAsync(
            string changedBy,
            string reason,
            CancellationToken cancellationToken)
    {
        return _inner.RecalculateAffectedVariantsAsync(
            changedBy,
            reason,
            cancellationToken);
    }

    public Task<EffectivePriceRecalculationResult>
        RecalculateAffectedVariantsAsync(
            string changedBy,
            string reason,
            string correlationId,
            CancellationToken cancellationToken)
    {
        return _inner.RecalculateAffectedVariantsAsync(
            changedBy,
            reason,
            correlationId,
            cancellationToken);
    }

    private static bool IsIncrease(
        PriceAdjustmentType type)
    {
        return type is
            PriceAdjustmentType.PercentIncrease
            or PriceAdjustmentType.AmountIncrease;
    }
}

public static class PriceIncreaseCalculator
{
    private const decimal MaximumMoney =
        9999999999999999m;
    private const decimal MaximumPercentIncrease =
        1000m;

    public static decimal Calculate(
        decimal listPrice,
        PriceAdjustmentType adjustmentType,
        decimal adjustmentValue,
        string sku)
    {
        if (listPrice <= 0)
        {
            throw new InvalidPriceIncreaseException(
                $"Giá niêm yết của SKU {sku} phải lớn hơn 0.");
        }

        if (adjustmentValue <= 0)
        {
            throw new InvalidPriceIncreaseException(
                $"Giá trị tăng của SKU {sku} phải lớn hơn 0.");
        }

        decimal rawPrice = adjustmentType switch
        {
            PriceAdjustmentType.PercentIncrease
                when adjustmentValue <= MaximumPercentIncrease
                => listPrice
                    * (1m + adjustmentValue / 100m),

            PriceAdjustmentType.PercentIncrease
                => throw new InvalidPriceIncreaseException(
                    $"Phần trăm tăng của SKU {sku} "
                    + $"không được vượt quá "
                    + $"{MaximumPercentIncrease:0}%."),

            PriceAdjustmentType.AmountIncrease
                => listPrice + adjustmentValue,

            _ => throw new InvalidPriceIncreaseException(
                $"Kiểu tăng giá của SKU {sku} không hợp lệ.")
        };

        var roundedPrice = decimal.Round(
            rawPrice,
            0,
            MidpointRounding.AwayFromZero);

        if (roundedPrice <= 0
            || roundedPrice > MaximumMoney)
        {
            throw new InvalidPriceIncreaseException(
                $"Giá tính được của SKU {sku} không hợp lệ.");
        }

        return roundedPrice;
    }
}

public sealed class InvalidPriceIncreaseException : Exception
{
    public InvalidPriceIncreaseException(string message)
        : base(message)
    {
    }
}
