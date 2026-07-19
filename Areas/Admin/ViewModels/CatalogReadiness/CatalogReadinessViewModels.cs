namespace WebApplication2.Areas.Admin.ViewModels.CatalogReadiness;

public sealed class CatalogReadinessPageViewModel
{
    public string Query { get; init; } = string.Empty;

    public string Status { get; init; } = "all";

    public int Page { get; init; }

    public int TotalPages { get; init; }

    public int TotalItems { get; init; }

    public CatalogReadinessSummaryViewModel Summary { get; init; } = new();

    public IReadOnlyList<CatalogReadinessItemViewModel> Items { get; init; } = [];
}

public sealed class CatalogReadinessSummaryViewModel
{
    public int TotalProductCount { get; init; }

    public int ReadyCount { get; init; }

    public int AttentionCount { get; init; }

    public int UnavailableCount { get; init; }

    public int HiddenCount { get; init; }
}

public sealed class CatalogReadinessItemViewModel
{
    public int ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public string ProductSlug { get; init; } = string.Empty;

    public string CategoryName { get; init; } = string.Empty;

    public string? BrandName { get; init; }

    public string ImageUrl { get; init; } = "/images/no-image.png";

    public bool ProductIsActive { get; init; }

    public bool HasMainImage { get; init; }

    public bool HasSearchContent { get; init; }

    public int ItemCount { get; init; }

    public int ActiveItemCount { get; init; }

    public int InStockItemCount { get; init; }

    public int RequiredOptionGroupCount { get; init; }

    public int RequiredOptionGroupWithoutValuesCount { get; init; }

    public int IncompleteActiveItemCount { get; init; }

    public int RequiredInformationCount { get; init; }

    public int MissingRequiredInformationCount { get; init; }

    public string State { get; init; } = "attention";

    public string StateLabel { get; init; } = "Cần hoàn thiện";

    public IReadOnlyList<string> Issues { get; init; } = [];
}
