using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Pricing;

public sealed class EffectivePriceService : IEffectivePriceService
{
    private const decimal MaximumMoney = 9999999999999999m;
    private const int ChangedByMaxLength = 100;
    private const int NoteMaxLength = 255;
    private const int ReasonMaxLength = 500;
    private const int CorrelationIdMaxLength = 64;

    private static readonly PriceCampaignStatus[] BlockingStatuses =
    [
        PriceCampaignStatus.Confirmed,
        PriceCampaignStatus.Scheduled,
        PriceCampaignStatus.Active
    ];

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public EffectivePriceService(
        ApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public Task<CampaignPricingValidationResult> ValidateCampaignAsync(
        int campaignId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        IReadOnlyCollection<CampaignPriceInput> items,
        CancellationToken cancellationToken)
    {
        return ValidateCampaignAsync(
            campaignId,
            PriceCampaignMode.FixedWindow,
            startDateUtc,
            endDateUtc,
            PriceConflictPolicy.Reject,
            items,
            cancellationToken);
    }

    public async Task<CampaignPricingValidationResult> ValidateCampaignAsync(
        int campaignId,
        PriceCampaignMode mode,
        DateTime startDateUtc,
        DateTime? endDateUtc,
        PriceConflictPolicy conflictPolicy,
        IReadOnlyCollection<CampaignPriceInput> items,
        CancellationToken cancellationToken)
    {
        var preview = await PreviewCampaignAsync(
            campaignId,
            mode,
            startDateUtc,
            endDateUtc,
            conflictPolicy,
            items.Select(item => new PricePlanPreviewInput(
                item.VariantId,
                PriceAdjustmentType.FixedPrice,
                item.NewPrice))
                .ToArray(),
            cancellationToken);

        if (!preview.IsValid || !preview.CanConfirm)
        {
            return CampaignPricingValidationResult.Failure(
                preview.ErrorMessage ?? "Dữ liệu giá không hợp lệ.",
                preview.ErrorCode ?? "PRICING_VALIDATION_FAILED",
                preview.Items
                    .Where(item => item.Conflicts.Count > 0 || item.IsStale)
                    .Select(item => item.VariantId)
                    .Distinct()
                    .ToArray());
        }

        return CampaignPricingValidationResult.Success;
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
        if (mode == PriceCampaignMode.FixedWindow
            && (!endDateUtc.HasValue || endDateUtc.Value <= startDateUtc))
        {
            return PricePlanPreviewResult.Failure(
                "Thời gian kết thúc phải sau thời gian bắt đầu.",
                "INVALID_DURATION");
        }

        if (mode == PriceCampaignMode.OpenEnded && endDateUtc.HasValue)
        {
            return PricePlanPreviewResult.Failure(
                "Kế hoạch không thời hạn không được có thời gian kết thúc.",
                "OPEN_ENDED_HAS_END_DATE");
        }

        if (items.Count == 0)
        {
            return PricePlanPreviewResult.Failure(
                "Vui lòng chọn ít nhất một biến thể.",
                "EMPTY_ITEMS");
        }

        var duplicateVariantIds = items
            .GroupBy(item => item.VariantId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id)
            .ToArray();

        if (duplicateVariantIds.Length > 0)
        {
            return PricePlanPreviewResult.Failure(
                $"Một biến thể chỉ được xuất hiện một lần. ID trùng: {string.Join(", ", duplicateVariantIds)}.",
                "DUPLICATE_VARIANT");
        }

        if (items.Any(item => item.VariantId <= 0))
        {
            return PricePlanPreviewResult.Failure(
                "Danh sách biến thể chứa ID không hợp lệ.",
                "INVALID_VARIANT_ID");
        }

        var variantIds = items
            .Select(item => item.VariantId)
            .Distinct()
            .ToArray();

        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Where(variant => variantIds.Contains(variant.Id))
            .Select(variant => new
            {
                variant.Id,
                variant.ProductId,
                ProductName = variant.Product.Name,
                variant.SKU,
                variant.Color,
                variant.Size,
                variant.Price,
                variant.CurrentPrice,
                variant.RowVersion,
                variant.IsActive,
                ProductIsActive = variant.Product.IsActive
            })
            .ToListAsync(cancellationToken);

        var variantsById = variants.ToDictionary(variant => variant.Id);
        var missingVariantIds = variantIds
            .Where(id => !variantsById.ContainsKey(id))
            .OrderBy(id => id)
            .ToArray();

        if (missingVariantIds.Length > 0)
        {
            return PricePlanPreviewResult.Failure(
                $"Không tìm thấy biến thể có ID: {string.Join(", ", missingVariantIds)}.",
                "VARIANT_NOT_FOUND");
        }

        var inactiveVariant = variants
            .FirstOrDefault(variant => !variant.IsActive || !variant.ProductIsActive);

        if (inactiveVariant is not null)
        {
            return PricePlanPreviewResult.Failure(
                $"SKU {inactiveVariant.SKU} đang ngừng hoạt động.",
                "VARIANT_INACTIVE");
        }

        var rangeEndUtc = endDateUtc ?? DateTime.MaxValue;

        var conflictRows = await _context.PriceCampaignItems
            .AsNoTracking()
            .Where(item =>
                variantIds.Contains(item.VariantId)
                && item.CampaignId != campaignId
                && BlockingStatuses.Contains(item.Campaign.Status)
                && item.Campaign.StartDate < rangeEndUtc
                && (!item.Campaign.EndDate.HasValue
                    || item.Campaign.EndDate.Value > startDateUtc))
            .Select(item => new
            {
                item.VariantId,
                item.CampaignId,
                item.Campaign.Code,
                item.Campaign.Name,
                item.Campaign.Status,
                item.Campaign.StartDate,
                item.Campaign.EndDate
            })
            .OrderBy(item => item.VariantId)
            .ThenBy(item => item.StartDate)
            .ThenBy(item => item.CampaignId)
            .ToListAsync(cancellationToken);

        var conflictsByVariantId = conflictRows
            .GroupBy(item => item.VariantId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PricePlanConflictResult>)group
                    .Select(item => new PricePlanConflictResult(
                        item.CampaignId,
                        item.Code,
                        item.Name,
                        item.Status,
                        item.StartDate,
                        item.EndDate))
                    .ToList());

        var previewItems = new List<PricePlanPreviewItemResult>(items.Count);

        try
        {
            foreach (var input in items)
            {
                var variant = variantsById[input.VariantId];
                var isStale = input.ExpectedRowVersion is { Length: > 0 }
                    && !variant.RowVersion.SequenceEqual(input.ExpectedRowVersion);
                var newPrice = CalculateTargetPrice(
                    variant.Price,
                    input.AdjustmentType,
                    input.AdjustmentValue,
                    variant.SKU);
                var deltaAmount = newPrice - variant.CurrentPrice;
                var deltaPercent = variant.CurrentPrice == 0
                    ? 0
                    : decimal.Round(
                        deltaAmount / variant.CurrentPrice * 100m,
                        2,
                        MidpointRounding.AwayFromZero);

                conflictsByVariantId.TryGetValue(
                    variant.Id,
                    out var conflicts);

                previewItems.Add(new PricePlanPreviewItemResult(
                    variant.ProductId,
                    variant.ProductName,
                    variant.Id,
                    variant.SKU,
                    string.Join(
                        " - ",
                        new[] { variant.Color, variant.Size }
                            .Where(value => !string.IsNullOrWhiteSpace(value))),
                    variant.RowVersion,
                    variant.Price,
                    variant.CurrentPrice,
                    newPrice,
                    deltaAmount,
                    deltaPercent,
                    input.AdjustmentType,
                    input.AdjustmentValue,
                    isStale,
                    conflicts ?? []));
            }
        }
        catch (InvalidPriceAdjustmentException exception)
        {
            return PricePlanPreviewResult.Failure(
                exception.Message,
                "INVALID_ADJUSTMENT");
        }

        var staleCount = previewItems.Count(item => item.IsStale);
        var conflictCount = previewItems.Count(item => item.Conflicts.Count > 0);
        var policyAvailable = conflictPolicy == PriceConflictPolicy.Reject;
        var isValid = staleCount == 0;
        var canConfirm = isValid
            && policyAvailable
            && (conflictPolicy != PriceConflictPolicy.Reject || conflictCount == 0);

        string? errorMessage = null;
        string? errorCode = null;

        if (staleCount > 0)
        {
            errorMessage = $"Có {staleCount} biến thể đã thay đổi sau khi được tải. Vui lòng tải lại dữ liệu.";
            errorCode = "STALE_VARIANT";
        }
        else if (!policyAvailable)
        {
            errorMessage = "Chính sách thay thế giá chưa được mở trong phase này. Hiện tại chỉ hỗ trợ từ chối xung đột.";
            errorCode = "CONFLICT_POLICY_NOT_AVAILABLE";
        }
        else if (conflictCount > 0)
        {
            errorMessage = $"Có {conflictCount} biến thể bị chồng lấn với kế hoạch giá khác.";
            errorCode = "PRICE_WINDOW_CONFLICT";
        }

        var summary = new PricePlanPreviewSummary(
            previewItems.Select(item => item.ProductId).Distinct().Count(),
            previewItems.Count,
            previewItems.Count(item => item.DeltaAmount > 0),
            previewItems.Count(item => item.DeltaAmount < 0),
            previewItems.Count(item => item.DeltaAmount == 0),
            conflictCount,
            staleCount,
            previewItems.Sum(item => item.CurrentPrice),
            previewItems.Sum(item => item.NewPrice));

        return new PricePlanPreviewResult(
            isValid,
            canConfirm,
            errorMessage,
            errorCode,
            previewItems
                .OrderBy(item => item.ProductName)
                .ThenBy(item => item.Sku)
                .ToList(),
            summary);
    }

    public Task<EffectivePriceRecalculationResult> RecalculateVariantsAsync(
        IReadOnlyCollection<int> variantIds,
        string changedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        return RecalculateVariantsAsync(
            variantIds,
            changedBy,
            reason,
            Guid.NewGuid().ToString("N"),
            cancellationToken);
    }

    public Task<EffectivePriceRecalculationResult> RecalculateVariantsAsync(
        IReadOnlyCollection<int> variantIds,
        string changedBy,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        return RecalculateVariantsAtAsync(
            variantIds,
            changedBy,
            reason,
            correlationId,
            nowUtc,
            cancellationToken);
    }

    public Task<EffectivePriceRecalculationResult> RecalculateAffectedVariantsAsync(
        string changedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        return RecalculateAffectedVariantsAsync(
            changedBy,
            reason,
            Guid.NewGuid().ToString("N"),
            cancellationToken);
    }

    public async Task<EffectivePriceRecalculationResult> RecalculateAffectedVariantsAsync(
        string changedBy,
        string reason,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var variantIds = await _context.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                variant.CurrentPrice != variant.Price
                || variant.CurrentPriceSourceType != EffectivePriceSourceType.ListPrice
                || variant.CampaignItems.Any(item =>
                    BlockingStatuses.Contains(item.Campaign.Status)
                    && item.Campaign.StartDate <= nowUtc
                    && (!item.Campaign.EndDate.HasValue
                        || item.Campaign.EndDate.Value > nowUtc)))
            .Select(variant => variant.Id)
            .ToListAsync(cancellationToken);

        return await RecalculateVariantsAtAsync(
            variantIds,
            changedBy,
            reason,
            correlationId,
            nowUtc,
            cancellationToken);
    }

    private async Task<EffectivePriceRecalculationResult> RecalculateVariantsAtAsync(
        IReadOnlyCollection<int> variantIds,
        string changedBy,
        string reason,
        string correlationId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var distinctVariantIds = variantIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (distinctVariantIds.Length == 0)
        {
            return new EffectivePriceRecalculationResult(0, 0);
        }

        var variants = await _context.ProductVariants
            .Where(variant => distinctVariantIds.Contains(variant.Id))
            .ToListAsync(cancellationToken);

        var campaignCandidates = await _context.PriceCampaignItems
            .AsNoTracking()
            .Where(item =>
                distinctVariantIds.Contains(item.VariantId)
                && BlockingStatuses.Contains(item.Campaign.Status)
                && item.Campaign.StartDate <= nowUtc
                && (!item.Campaign.EndDate.HasValue
                    || item.Campaign.EndDate.Value > nowUtc)
                && item.NewPrice > 0)
            .Select(item => new EffectiveCampaignCandidate(
                item.VariantId,
                item.CampaignId,
                item.Campaign.Name,
                item.Campaign.SourceType,
                item.Campaign.StartDate,
                item.Campaign.EndDate,
                item.NewPrice))
            .OrderBy(candidate => candidate.VariantId)
            .ThenByDescending(candidate => candidate.StartDate)
            .ThenByDescending(candidate => candidate.CampaignId)
            .ToListAsync(cancellationToken);

        // Overlap is rejected on write. This deterministic fallback protects legacy data.
        var winnerByVariantId = campaignCandidates
            .GroupBy(candidate => candidate.VariantId)
            .ToDictionary(group => group.Key, group => group.First());

        var normalizedChangedBy = TruncateRequired(
            changedBy,
            ChangedByMaxLength,
            "System");
        var normalizedReason = TruncateRequired(
            reason,
            ReasonMaxLength,
            "Price projection");
        var normalizedCorrelationId = TruncateRequired(
            correlationId,
            CorrelationIdMaxLength,
            Guid.NewGuid().ToString("N"));

        var changedCount = 0;

        foreach (var variant in variants)
        {
            winnerByVariantId.TryGetValue(variant.Id, out var winner);

            var effectivePrice = winner?.NewPrice ?? variant.Price;
            var desiredSourceType = winner is null
                ? EffectivePriceSourceType.ListPrice
                : EffectivePriceSourceType.Campaign;
            var desiredSourceId = winner?.CampaignId;
            var desiredEffectiveFrom = winner?.StartDate;
            var desiredEffectiveTo = winner?.EndDate;

            var priceChanged = variant.CurrentPrice != effectivePrice;
            var sourceChanged =
                variant.CurrentPriceSourceType != desiredSourceType
                || variant.CurrentPriceSourceId != desiredSourceId
                || variant.CurrentPriceEffectiveFrom != desiredEffectiveFrom
                || variant.CurrentPriceEffectiveTo != desiredEffectiveTo;

            if (!priceChanged && !sourceChanged)
            {
                continue;
            }

            var eventType = ResolveHistoryEventType(variant, winner);
            var sourceType = winner?.SourceType ?? PriceChangeSourceType.System;
            var priceSource = winner is null
                ? "Khôi phục giá niêm yết"
                : $"Áp dụng kế hoạch {winner.CampaignName} (#{winner.CampaignId})";

            _context.PriceHistories.Add(new PriceHistory
            {
                ProductVariantId = variant.Id,
                OldPrice = variant.CurrentPrice,
                NewPrice = effectivePrice,
                EventType = eventType,
                SourceType = sourceType,
                SourceId = desiredSourceId,
                CorrelationId = normalizedCorrelationId,
                Reason = normalizedReason,
                EffectiveFrom = desiredEffectiveFrom,
                EffectiveTo = desiredEffectiveTo,
                ChangedBy = normalizedChangedBy,
                Note = TruncateRequired(
                    $"{normalizedReason}. {priceSource}.",
                    NoteMaxLength,
                    priceSource),
                CreatedAt = nowUtc
            });

            variant.CurrentPrice = effectivePrice;
            variant.CurrentPriceSourceType = desiredSourceType;
            variant.CurrentPriceSourceId = desiredSourceId;
            variant.CurrentPriceEffectiveFrom = desiredEffectiveFrom;
            variant.CurrentPriceEffectiveTo = desiredEffectiveTo;
            variant.UpdatedAt = nowUtc;
            changedCount++;
        }

        if (changedCount > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new EffectivePriceRecalculationResult(
            variants.Count,
            changedCount);
    }

    private static PriceHistoryEventType ResolveHistoryEventType(
        ProductVariant variant,
        EffectiveCampaignCandidate? winner)
    {
        if (winner is null)
        {
            return PriceHistoryEventType.Restored;
        }

        if (variant.CurrentPriceSourceType == EffectivePriceSourceType.Campaign
            && variant.CurrentPriceSourceId.HasValue
            && variant.CurrentPriceSourceId.Value != winner.CampaignId)
        {
            return PriceHistoryEventType.Replaced;
        }

        return PriceHistoryEventType.Applied;
    }


    private static decimal CalculateTargetPrice(
        decimal listPrice,
        PriceAdjustmentType adjustmentType,
        decimal adjustmentValue,
        string sku)
    {
        if (adjustmentValue <= 0)
        {
            throw new InvalidPriceAdjustmentException(
                $"Giá trị điều chỉnh của SKU {sku} phải lớn hơn 0.");
        }

        decimal rawPrice = adjustmentType switch
        {
            PriceAdjustmentType.FixedPrice => adjustmentValue,
            PriceAdjustmentType.PercentOff
                when adjustmentValue < 100m
                => listPrice * (1m - adjustmentValue / 100m),
            PriceAdjustmentType.PercentOff
                => throw new InvalidPriceAdjustmentException(
                    $"Phần trăm giảm của SKU {sku} phải nhỏ hơn 100%."),
            PriceAdjustmentType.AmountOff
                when adjustmentValue < listPrice
                => listPrice - adjustmentValue,
            PriceAdjustmentType.AmountOff
                => throw new InvalidPriceAdjustmentException(
                    $"Số tiền giảm của SKU {sku} phải nhỏ hơn giá niêm yết."),
            _ => throw new InvalidPriceAdjustmentException(
                $"Kiểu điều chỉnh giá của SKU {sku} không hợp lệ.")
        };

        var roundedPrice = decimal.Round(
            rawPrice,
            0,
            MidpointRounding.AwayFromZero);

        if (roundedPrice <= 0 || roundedPrice > MaximumMoney)
        {
            throw new InvalidPriceAdjustmentException(
                $"Giá tính được của SKU {sku} không hợp lệ.");
        }

        return roundedPrice;
    }

    private sealed class InvalidPriceAdjustmentException : Exception
    {
        public InvalidPriceAdjustmentException(string message)
            : base(message)
        {
        }
    }

    private static string TruncateRequired(
        string? value,
        int maxLength,
        string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private sealed record EffectiveCampaignCandidate(
        int VariantId,
        int CampaignId,
        string CampaignName,
        PriceChangeSourceType SourceType,
        DateTime StartDate,
        DateTime? EndDate,
        decimal NewPrice);
}
