using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Pricing;

/// <summary>
/// Ngưỡng kiểm soát nội bộ cho nghiệp vụ giá.
/// Đây là guardrail vận hành, không phải diễn giải pháp lý.
/// </summary>
public static class PriceRiskPolicy
{
    public const decimal CriticalDiscountPercent = 90m;
    public const decimal SecondApproverDiscountPercent = 50m;
    public const decimal ThirtyDayLowUndercutPercent = 20m;
    public const int FrequentEffectiveChanges7Days = 5;
    public const int LargeScopeVariantCount = 50;

    public static PriceRiskAssessment Assess(
        PriceRiskCampaignInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var issues = new List<PriceRiskIssue>();

        if (input.Items.Count == 0)
        {
            issues.Add(new PriceRiskIssue(
                "EMPTY_SCOPE",
                PriceRiskIssueSeverity.Critical,
                "Kế hoạch không có SKU",
                "Không thể kiểm soát hoặc xác nhận một kế hoạch không có phạm vi giá."));
        }

        if (input.Mode == PriceCampaignMode.OpenEnded)
        {
            issues.Add(new PriceRiskIssue(
                "OPEN_ENDED_CAMPAIGN",
                PriceRiskIssueSeverity.High,
                "Kế hoạch không có ngày kết thúc",
                "Giá có thể được giữ vô thời hạn; cần một quản trị viên khác xác nhận."));
        }

        if (input.Items.Count >= LargeScopeVariantCount)
        {
            issues.Add(new PriceRiskIssue(
                "LARGE_SCOPE",
                PriceRiskIssueSeverity.High,
                "Phạm vi thay đổi giá lớn",
                $"Kế hoạch tác động {input.Items.Count} SKU, từ ngưỡng "
                + $"{LargeScopeVariantCount} SKU trở lên."));
        }

        if (input.OverlapCampaignCount > 0)
        {
            var severity = input.ConflictPolicy == PriceConflictPolicy.Reject
                ? PriceRiskIssueSeverity.Critical
                : PriceRiskIssueSeverity.Warning;

            issues.Add(new PriceRiskIssue(
                "OVERLAPPING_CAMPAIGNS",
                severity,
                "Có kế hoạch chồng lấn",
                $"{input.OverlapCampaignCount} kế hoạch khác giao nhau về thời gian "
                + "và phạm vi SKU."));
        }

        foreach (var item in input.Items)
        {
            var discountPercent = DiscountPercent(
                item.ListPrice,
                item.NewPrice);

            if (discountPercent >= CriticalDiscountPercent)
            {
                issues.Add(new PriceRiskIssue(
                    "CRITICAL_DISCOUNT",
                    PriceRiskIssueSeverity.Critical,
                    $"Mức giảm cực lớn trên {item.Sku}",
                    $"Giá mới thấp hơn giá niêm yết {discountPercent:0.#}%, "
                    + $"chạm ngưỡng chặn {CriticalDiscountPercent:0.#}%.",
                    item.VariantId,
                    item.Sku));
            }
            else if (discountPercent >= SecondApproverDiscountPercent)
            {
                issues.Add(new PriceRiskIssue(
                    "HIGH_DISCOUNT",
                    PriceRiskIssueSeverity.High,
                    $"Mức giảm lớn trên {item.Sku}",
                    $"Giá mới thấp hơn giá niêm yết {discountPercent:0.#}%; "
                    + "kế hoạch cần người xác nhận thứ hai.",
                    item.VariantId,
                    item.Sku));
            }

            if (input.SourceType == PriceChangeSourceType.Promotion
                && item.NewPrice >= item.ListPrice)
            {
                issues.Add(new PriceRiskIssue(
                    "PROMOTION_NOT_LOWER_THAN_LIST",
                    PriceRiskIssueSeverity.Critical,
                    $"Giá khuyến mại không thấp hơn giá niêm yết trên {item.Sku}",
                    "Nguồn Promotion phải tạo ra giá thấp hơn giá niêm yết.",
                    item.VariantId,
                    item.Sku));
            }

            var undercutPercent = UndercutPercent(
                item.LowestEffectivePrice30Days,
                item.NewPrice);

            if (undercutPercent >= ThirtyDayLowUndercutPercent)
            {
                issues.Add(new PriceRiskIssue(
                    "THIRTY_DAY_LOW_UNDERCUT",
                    PriceRiskIssueSeverity.High,
                    $"Giá mới thấp hơn sâu so với 30 ngày trên {item.Sku}",
                    $"Giá mới thấp hơn mức thấp nhất quan sát 30 ngày "
                    + $"{undercutPercent:0.#}%.",
                    item.VariantId,
                    item.Sku));
            }
            else if (undercutPercent > 0)
            {
                issues.Add(new PriceRiskIssue(
                    "NEW_THIRTY_DAY_LOW",
                    PriceRiskIssueSeverity.Warning,
                    $"Tạo mức thấp mới trong 30 ngày trên {item.Sku}",
                    $"Giá mới thấp hơn mức thấp nhất quan sát "
                    + $"{undercutPercent:0.#}%.",
                    item.VariantId,
                    item.Sku));
            }

            if (item.EffectiveChangeCount7Days
                >= FrequentEffectiveChanges7Days)
            {
                issues.Add(new PriceRiskIssue(
                    "FREQUENT_PRICE_CHANGES",
                    PriceRiskIssueSeverity.High,
                    $"SKU {item.Sku} thay đổi giá quá thường xuyên",
                    $"Có {item.EffectiveChangeCount7Days} lần đổi giá hiệu lực "
                    + "trong 7 ngày gần nhất.",
                    item.VariantId,
                    item.Sku));
            }

            if (item.SnapshotStale)
            {
                issues.Add(new PriceRiskIssue(
                    "STALE_DRAFT_SNAPSHOT",
                    PriceRiskIssueSeverity.High,
                    $"Snapshot bản nháp đã cũ trên {item.Sku}",
                    "Giá niêm yết hoặc giá hiệu lực hiện tại không còn khớp "
                    + "snapshot lúc lưu bản nháp.",
                    item.VariantId,
                    item.Sku));
            }
        }

        var maxDiscount = input.Items.Count == 0
            ? 0m
            : input.Items.Max(item =>
                DiscountPercent(item.ListPrice, item.NewPrice));

        var maxUndercut = input.Items.Count == 0
            ? 0m
            : input.Items.Max(item =>
                UndercutPercent(
                    item.LowestEffectivePrice30Days,
                    item.NewPrice));

        var frequentVariantCount = input.Items.Count(item =>
            item.EffectiveChangeCount7Days
                >= FrequentEffectiveChanges7Days);

        var staleSnapshotCount = input.Items.Count(item =>
            item.SnapshotStale);

        var baseRequiresSecondApprover =
            issues.Any(issue =>
                issue.Severity is PriceRiskIssueSeverity.High
                    or PriceRiskIssueSeverity.Critical);

        var sameCreatorAndConfirmer =
            SameActor(input.CreatedBy, input.ConfirmedBy);

        if (input.Status != PriceCampaignStatus.Draft
            && baseRequiresSecondApprover
            && sameCreatorAndConfirmer)
        {
            issues.Add(new PriceRiskIssue(
                "MAKER_CHECKER_VIOLATION",
                PriceRiskIssueSeverity.Critical,
                "Người tạo đồng thời là người xác nhận",
                "Kế hoạch rủi ro cao không đáp ứng nguyên tắc maker-checker."));
        }

        var currentActorIsCreator =
            SameActor(input.CreatedBy, input.CurrentActor);

        if (input.Status == PriceCampaignStatus.Draft
            && baseRequiresSecondApprover
            && currentActorIsCreator)
        {
            issues.Add(new PriceRiskIssue(
                "SECOND_APPROVER_REQUIRED",
                PriceRiskIssueSeverity.High,
                "Cần quản trị viên khác xác nhận",
                "Người tạo bản nháp không nên tự xác nhận kế hoạch rủi ro cao."));
        }

        var highestSeverity = issues.Count == 0
            ? PriceRiskIssueSeverity.Info
            : issues.Max(issue => issue.Severity);

        var level = highestSeverity switch
        {
            PriceRiskIssueSeverity.Critical =>
                PriceRiskLevel.Critical,
            PriceRiskIssueSeverity.High =>
                PriceRiskLevel.High,
            PriceRiskIssueSeverity.Warning =>
                PriceRiskLevel.Medium,
            _ => PriceRiskLevel.Low
        };

        var hasBlockingIssues = issues.Any(issue =>
            issue.Severity == PriceRiskIssueSeverity.Critical);

        var requiresSecondApprover =
            baseRequiresSecondApprover;

        var canCurrentActorConfirm =
            !hasBlockingIssues
            && (!requiresSecondApprover
                || !currentActorIsCreator);

        return new PriceRiskAssessment(
            level,
            issues,
            hasBlockingIssues,
            requiresSecondApprover,
            canCurrentActorConfirm,
            Math.Round(maxDiscount, 1),
            Math.Round(maxUndercut, 1),
            frequentVariantCount,
            staleSnapshotCount);
    }

    public static decimal DiscountPercent(
        decimal listPrice,
        decimal newPrice)
    {
        if (listPrice <= 0 || newPrice >= listPrice)
        {
            return 0m;
        }

        return (listPrice - newPrice)
            * 100m
            / listPrice;
    }

    public static decimal UndercutPercent(
        decimal lowestObservedPrice,
        decimal newPrice)
    {
        if (lowestObservedPrice <= 0
            || newPrice >= lowestObservedPrice)
        {
            return 0m;
        }

        return (lowestObservedPrice - newPrice)
            * 100m
            / lowestObservedPrice;
    }

    private static bool SameActor(
        string? left,
        string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && string.Equals(
                left.Trim(),
                right.Trim(),
                StringComparison.OrdinalIgnoreCase);
    }
}

public enum PriceRiskLevel
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum PriceRiskIssueSeverity
{
    Info = 1,
    Warning = 2,
    High = 3,
    Critical = 4
}

public sealed record PriceRiskIssue(
    string Code,
    PriceRiskIssueSeverity Severity,
    string Title,
    string Detail,
    int? VariantId = null,
    string? Sku = null);

public sealed record PriceRiskItemInput(
    int VariantId,
    string Sku,
    decimal ListPrice,
    decimal CurrentPrice,
    decimal NewPrice,
    decimal LowestEffectivePrice30Days,
    int EffectiveChangeCount7Days,
    bool SnapshotStale);

public sealed record PriceRiskCampaignInput(
    int CampaignId,
    PriceCampaignStatus Status,
    PriceCampaignMode Mode,
    PriceChangeSourceType SourceType,
    PriceConflictPolicy ConflictPolicy,
    string CreatedBy,
    string? ConfirmedBy,
    string CurrentActor,
    int OverlapCampaignCount,
    IReadOnlyCollection<PriceRiskItemInput> Items);

public sealed record PriceRiskAssessment(
    PriceRiskLevel Level,
    IReadOnlyList<PriceRiskIssue> Issues,
    bool HasBlockingIssues,
    bool RequiresSecondApprover,
    bool CanCurrentActorConfirm,
    decimal MaximumDiscountPercent,
    decimal MaximumThirtyDayLowUndercutPercent,
    int FrequentVariantCount,
    int StaleSnapshotCount);
