using WebApplication2.Models.Enums;

namespace WebApplication2.Areas.Admin.ViewModels.PriceHistories;

public sealed class PriceHistoryIndexPageViewModel
{
    public DateTime GeneratedAtUtc { get; init; }

    public PriceHistoryFilterViewModel Filter { get; init; } = new();

    public IReadOnlyList<PriceHistoryProductOptionViewModel>
        ProductOptions { get; init; } = [];

    public IReadOnlyList<PriceHistoryVariantOptionViewModel>
        VariantOptions { get; init; } = [];

    public IReadOnlyList<PriceHistoryProductListItemViewModel>
        ProductCatalog { get; init; } = [];

    public PriceHistorySelectedVariantViewModel?
        SelectedVariant { get; init; }

    public PriceHistorySummaryViewModel?
        Summary { get; init; }

    public PriceHistoryChartViewModel
        Chart { get; init; } = new();

    public IReadOnlyList<PriceHistoryCampaignBandViewModel>
        Campaigns { get; init; } = [];

    public IReadOnlyList<PriceHistoryEventRowViewModel>
        Events { get; init; } = [];

    public int TotalEvents { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 40;
    public int TotalPages { get; init; }
}

public sealed class PriceHistoryFilterViewModel
{
    public int? ProductId { get; init; }
    public int? VariantId { get; init; }
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public PriceHistoryKind? PriceKind { get; init; }
    public PriceChangeSourceType? SourceType { get; init; }
    public string Search { get; init; } = string.Empty;
    public string? Notice { get; init; }
    public int DayCount { get; init; }
}

public sealed record PriceHistoryProductOptionViewModel(
    int Id,
    string Name);

public sealed record PriceHistoryVariantOptionViewModel(
    int Id,
    int ProductId,
    string ProductName,
    string Sku,
    string Description,
    bool IsActive,
    bool HasHistory);


public sealed class PriceHistoryProductListItemViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public string? BrandName { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsActive { get; init; }
    public int VariantCount { get; init; }
    public int HistoryEventCount { get; init; }
    public bool IsSelected { get; init; }

    public IReadOnlyList<PriceHistoryVariantListItemViewModel>
        Variants { get; init; } = [];
}

public sealed class PriceHistoryVariantListItemViewModel
{
    public int Id { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public bool HasHistory { get; init; }
    public int HistoryEventCount { get; init; }
    public decimal ListPrice { get; init; }
    public decimal CurrentPrice { get; init; }
    public EffectivePriceSourceType CurrentSourceType { get; init; }
    public DateTime? LastChangedAtLocal { get; init; }
    public bool IsSelected { get; init; }
}

public sealed class PriceHistorySelectedVariantViewModel
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public int VariantId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public decimal ListPrice { get; init; }
    public decimal CurrentPrice { get; init; }
    public EffectivePriceSourceType CurrentSourceType { get; init; }
    public int? CurrentSourceId { get; init; }
    public string CurrentSourceLabel { get; init; } = string.Empty;
    public string? CurrentCampaignName { get; init; }
}

public sealed class PriceHistorySummaryViewModel
{
    public string Currency { get; init; } = "VND";
    public decimal ListPrice { get; init; }
    public decimal CurrentPrice { get; init; }
    public decimal LowestEffectivePrice30Days { get; init; }
    public decimal HighestEffectivePrice30Days { get; init; }
    public int EffectiveChangeCount30Days { get; init; }
    public decimal LowestEffectivePriceInRange { get; init; }
    public decimal HighestEffectivePriceInRange { get; init; }
    public int EffectiveChangeCountInRange { get; init; }
    public int FilteredEventCount { get; init; }
    public decimal DifferenceFromListPrice { get; init; }
    public decimal DiscountPercentFromListPrice { get; init; }
}

public sealed class PriceHistoryChartViewModel
{
    public bool HasHistoryEvents { get; init; }
    public bool IsTruncated { get; init; }
    public decimal MinimumPrice { get; init; }
    public decimal MaximumPrice { get; init; }

    public IReadOnlyList<PriceHistoryChartSeriesViewModel>
        Series { get; init; } = [];

    public IReadOnlyList<PriceHistoryChartAxisLabelViewModel>
        YAxisLabels { get; init; } = [];

    public IReadOnlyList<PriceHistoryChartAxisLabelViewModel>
        XAxisLabels { get; init; } = [];
}

public sealed class PriceHistoryChartSeriesViewModel
{
    public PriceHistoryKind Kind { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Tone { get; init; } = string.Empty;
    public string SvgPath { get; init; } = string.Empty;

    public IReadOnlyList<PriceHistoryChartPointViewModel>
        Points { get; init; } = [];
}

public sealed class PriceHistoryChartPointViewModel
{
    public int EventId { get; init; }
    public decimal X { get; init; }
    public decimal Y { get; init; }
    public DateTime EffectiveAtLocal { get; init; }
    public decimal Value { get; init; }
    public string Label { get; init; } = string.Empty;
}

public sealed class PriceHistoryChartAxisLabelViewModel
{
    public decimal Position { get; init; }
    public string Label { get; init; } = string.Empty;
}

public sealed class PriceHistoryCampaignBandViewModel
{
    public int CampaignId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public PriceCampaignStatus Status { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public string Tone { get; init; } = "neutral";
    public DateTime StartLocal { get; init; }
    public DateTime? EndLocal { get; init; }
    public decimal LeftPercent { get; init; }
    public decimal WidthPercent { get; init; }
}

public sealed class PriceHistoryEventRowViewModel
{
    public int Id { get; init; }
    public PriceHistoryKind PriceKind { get; init; }
    public string PriceKindLabel { get; init; } = string.Empty;
    public string PriceKindTone { get; init; } = "info";
    public string Currency { get; init; } = "VND";
    public decimal OldPrice { get; init; }
    public decimal NewPrice { get; init; }
    public decimal Difference { get; init; }
    public decimal DifferencePercent { get; init; }
    public string DirectionTone { get; init; } = "neutral";
    public PriceHistoryEventType EventType { get; init; }
    public string EventLabel { get; init; } = string.Empty;
    public PriceChangeSourceType SourceType { get; init; }
    public string SourceLabel { get; init; } = string.Empty;
    public int? SourceId { get; init; }
    public string? CampaignName { get; init; }
    public string? CampaignCode { get; init; }
    public DateTime EffectiveFromLocal { get; init; }
    public DateTime? EffectiveToLocal { get; init; }
    public DateTime RecordedAtLocal { get; init; }
    public string ChangedBy { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public string? CorrelationId { get; init; }
}
