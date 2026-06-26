namespace WebApplication2.Areas.Admin.ViewModels.Home;

public sealed class AdminDashboardViewModel
{
    public int ActiveProductCount { get; init; }

    public int ActiveVariantCount { get; init; }

    public int LowStockVariantCount { get; init; }

    public int OutOfStockVariantCount { get; init; }

    public int RunningCampaignCount { get; init; }

    public int UpcomingCampaignCount { get; init; }

    public IReadOnlyList<AdminDashboardLowStockItemViewModel> LowStockItems { get; init; } = [];

    public IReadOnlyList<AdminDashboardCampaignItemViewModel> Campaigns { get; init; } = [];
}

public sealed class AdminDashboardLowStockItemViewModel
{
    public int ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public int VariantId { get; init; }

    public string SKU { get; init; } = string.Empty;

    public int StockQuantity { get; init; }

    public decimal CurrentPrice { get; init; }
}

public sealed class AdminDashboardCampaignItemViewModel
{
    public int CampaignId { get; init; }

    public string Name { get; init; } = string.Empty;

    public DateTime StartDate { get; init; }

    public DateTime EndDate { get; init; }

    public bool IsRunning { get; init; }

    public int VariantCount { get; init; }
}
