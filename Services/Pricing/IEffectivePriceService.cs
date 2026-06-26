using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Pricing;

public interface IEffectivePriceService
{
    Task<CampaignPricingValidationResult> ValidateCampaignAsync(
        int campaignId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        IReadOnlyCollection<CampaignPriceInput> items,
        CancellationToken cancellationToken);

    Task<CampaignPricingValidationResult> ValidateCampaignAsync(
        int campaignId,
        PriceCampaignMode mode,
        DateTime startDateUtc,
        DateTime? endDateUtc,
        PriceConflictPolicy conflictPolicy,
        IReadOnlyCollection<CampaignPriceInput> items,
        CancellationToken cancellationToken);

    Task<PricePlanPreviewResult> PreviewCampaignAsync(
        int campaignId,
        PriceCampaignMode mode,
        DateTime startDateUtc,
        DateTime? endDateUtc,
        PriceConflictPolicy conflictPolicy,
        IReadOnlyCollection<PricePlanPreviewInput> items,
        CancellationToken cancellationToken);

    Task<EffectivePriceRecalculationResult> RecalculateVariantsAsync(
        IReadOnlyCollection<int> variantIds,
        string changedBy,
        string reason,
        CancellationToken cancellationToken);

    Task<EffectivePriceRecalculationResult> RecalculateVariantsAsync(
        IReadOnlyCollection<int> variantIds,
        string changedBy,
        string reason,
        string correlationId,
        CancellationToken cancellationToken);

    Task<EffectivePriceRecalculationResult> RecalculateAffectedVariantsAsync(
        string changedBy,
        string reason,
        CancellationToken cancellationToken);

    Task<EffectivePriceRecalculationResult> RecalculateAffectedVariantsAsync(
        string changedBy,
        string reason,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed record CampaignPriceInput(int VariantId, decimal NewPrice);

public sealed record PricePlanPreviewInput(
    int VariantId,
    PriceAdjustmentType AdjustmentType,
    decimal AdjustmentValue,
    byte[]? ExpectedRowVersion = null);

public sealed record PricePlanConflictResult(
    int CampaignId,
    string CampaignCode,
    string CampaignName,
    PriceCampaignStatus Status,
    DateTime StartDateUtc,
    DateTime? EndDateUtc);

public sealed record PricePlanPreviewItemResult(
    int ProductId,
    string ProductName,
    int VariantId,
    string Sku,
    string Attributes,
    byte[] RowVersion,
    decimal ListPrice,
    decimal CurrentPrice,
    decimal NewPrice,
    decimal DeltaAmount,
    decimal DeltaPercent,
    PriceAdjustmentType AdjustmentType,
    decimal AdjustmentValue,
    bool IsStale,
    IReadOnlyList<PricePlanConflictResult> Conflicts);

public sealed record PricePlanPreviewSummary(
    int ProductCount,
    int VariantCount,
    int IncreaseCount,
    int DecreaseCount,
    int UnchangedCount,
    int ConflictCount,
    int StaleCount,
    decimal CurrentTotal,
    decimal NewTotal);

public sealed record PricePlanPreviewResult(
    bool IsValid,
    bool CanConfirm,
    string? ErrorMessage,
    string? ErrorCode,
    IReadOnlyList<PricePlanPreviewItemResult> Items,
    PricePlanPreviewSummary Summary)
{
    public static PricePlanPreviewResult Failure(
        string errorMessage,
        string errorCode)
    {
        return new(
            false,
            false,
            errorMessage,
            errorCode,
            [],
            new PricePlanPreviewSummary(0, 0, 0, 0, 0, 0, 0, 0, 0));
    }
}

public sealed record CampaignPricingValidationResult(
    bool IsValid,
    string? ErrorMessage,
    string? ErrorCode = null,
    IReadOnlyCollection<int>? ConflictVariantIds = null)
{
    public static CampaignPricingValidationResult Success { get; }
        = new(true, null);

    public static CampaignPricingValidationResult Failure(
        string errorMessage,
        string errorCode,
        IReadOnlyCollection<int>? conflictVariantIds = null)
    {
        return new(false, errorMessage, errorCode, conflictVariantIds);
    }
}

public sealed record EffectivePriceRecalculationResult(
    int EvaluatedCount,
    int ChangedCount);
