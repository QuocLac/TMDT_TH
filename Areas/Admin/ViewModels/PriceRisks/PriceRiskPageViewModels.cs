using WebApplication2.Models.Enums;
using WebApplication2.Services.Pricing;

namespace WebApplication2.Areas.Admin.ViewModels.PriceRisks;

public sealed class PriceRiskIndexPageViewModel
{
    public DateTime GeneratedAtUtc { get; init; }
    public string CurrentActor { get; init; } = string.Empty;
    public PriceRiskFilterViewModel Filter { get; init; } = new();
    public PriceRiskSummaryViewModel Summary { get; init; } = new();
    public PriceRiskPolicyViewModel Policy { get; init; } = new();

    public IReadOnlyList<PriceRiskCampaignRowViewModel>
        Campaigns { get; init; } = [];

    public IReadOnlyList<PriceProjectionDriftRowViewModel>
        ProjectionDrifts { get; init; } = [];

    public int TotalProjectionDrifts { get; init; }
}

public sealed class PriceRiskFilterViewModel
{
    public string Search { get; init; } = string.Empty;
    public PriceRiskLevel? RiskLevel { get; init; }
}

public sealed class PriceRiskSummaryViewModel
{
    public int CampaignCount { get; init; }
    public int CriticalCampaignCount { get; init; }
    public int HighCampaignCount { get; init; }
    public int RequiresSecondApproverCount { get; init; }
    public int ProjectionDriftCount { get; init; }
    public int AmbiguousEffectivePriceCount { get; init; }
}

public sealed class PriceRiskPolicyViewModel
{
    public decimal CriticalDiscountPercent { get; init; }
    public decimal SecondApproverDiscountPercent { get; init; }
    public decimal ThirtyDayLowUndercutPercent { get; init; }
    public int FrequentEffectiveChanges7Days { get; init; }
    public int LargeScopeVariantCount { get; init; }
}

public sealed class PriceRiskCampaignRowViewModel
{
    public int CampaignId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public PriceCampaignStatus Status { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public DateTime StartLocal { get; init; }
    public DateTime? EndLocal { get; init; }
    public int VariantCount { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public string? ConfirmedBy { get; init; }
    public PriceRiskLevel RiskLevel { get; init; }
    public string RiskLabel { get; init; } = string.Empty;
    public string RiskTone { get; init; } = "neutral";
    public bool RequiresSecondApprover { get; init; }
    public bool CanCurrentActorConfirm { get; init; }
    public string ControlLabel { get; init; } = string.Empty;
    public decimal MaximumDiscountPercent { get; init; }
    public decimal MaximumThirtyDayLowUndercutPercent { get; init; }
    public int FrequentVariantCount { get; init; }
    public int StaleSnapshotCount { get; init; }
    public int OverlapCampaignCount { get; init; }

    public IReadOnlyList<PriceRiskIssueViewModel>
        Issues { get; init; } = [];
}

public sealed class PriceRiskIssueViewModel
{
    public string Code { get; init; } = string.Empty;
    public PriceRiskIssueSeverity Severity { get; init; }
    public string SeverityLabel { get; init; } = string.Empty;
    public string Tone { get; init; } = "neutral";
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public int? VariantId { get; init; }
    public string? Sku { get; init; }
}

public sealed class PriceProjectionDriftRowViewModel
{
    public int VariantId { get; init; }
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public decimal ListPrice { get; init; }
    public decimal StoredCurrentPrice { get; init; }
    public decimal? ExpectedCurrentPrice { get; init; }
    public EffectivePriceSourceType StoredSourceType { get; init; }
    public int? StoredSourceId { get; init; }
    public EffectivePriceSourceType? ExpectedSourceType { get; init; }
    public int? ExpectedSourceId { get; init; }
    public int EffectiveCampaignCount { get; init; }
    public bool IsAmbiguous { get; init; }
    public string Issue { get; init; } = string.Empty;
    public string Tone { get; init; } = "warning";
}

public sealed class PriceProjectionReconcileRequest
{
    public IReadOnlyCollection<int> VariantIds { get; init; } = [];
    public string? Reason { get; init; }
}
