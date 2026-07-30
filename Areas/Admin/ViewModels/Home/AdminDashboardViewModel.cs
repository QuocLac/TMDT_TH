using WebApplication2.Models.Enums;

namespace WebApplication2.Areas.Admin.ViewModels.Home;

public sealed class AdminDashboardViewModel
{
    public DateTime GeneratedAtUtc { get; init; }

    public AdminDashboardFilterViewModel Filter { get; init; } = new();

    public IReadOnlyList<AdminDashboardKpiViewModel> Kpis { get; init; } = [];
    public IReadOnlyList<AdminDashboardTrendPointViewModel> Trend { get; init; } = [];
    public IReadOnlyList<AdminDashboardBreakdownItemViewModel> OrderStatuses { get; init; } = [];
    public IReadOnlyList<AdminDashboardBreakdownItemViewModel> PaymentMethods { get; init; } = [];
    public IReadOnlyList<AdminDashboardTopProductViewModel> TopProducts { get; init; } = [];

    public decimal PaidRevenue { get; init; }
    public decimal RefundAmount { get; init; }
    public decimal NetRevenue { get; init; }
    public decimal AverageOrderValue { get; init; }
    public int OrdersInPeriod { get; init; }
    public int PaidOrdersInPeriod { get; init; }
    public int ReturnRequestsInPeriod { get; init; }
    public int CancelledOrdersInPeriod { get; init; }

    // Kept for compatibility with earlier dashboard code.
    public int OrdersTodayCount { get; init; }
    public decimal PaidRevenueToday { get; init; }

    public int AwaitingConfirmationCount { get; init; }
    public int ReadyToShipCount { get; init; }
    public int ShippingExceptionCount { get; init; }
    public int OpenReturnCount { get; init; }

    public int ActiveProductCount { get; init; }
    public int LowStockVariantCount { get; init; }
    public int OutOfStockVariantCount { get; init; }
    public int RunningCampaignCount { get; init; }

    public IReadOnlyList<AdminDashboardRecentOrderViewModel> RecentOrders { get; init; } = [];
    public IReadOnlyList<AdminDashboardLowStockItemViewModel> LowStockItems { get; init; } = [];
    public IReadOnlyList<AdminDashboardCampaignItemViewModel> Campaigns { get; init; } = [];
}

public sealed class AdminDashboardFilterViewModel
{
    public string Preset { get; init; } = "last7";
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public DateOnly PreviousFromDate { get; init; }
    public DateOnly PreviousToDate { get; init; }
    public string RangeLabel { get; init; } = string.Empty;
    public string PreviousRangeLabel { get; init; } = string.Empty;
    public int DayCount { get; init; }
    public string? Notice { get; init; }
}

public enum AdminDashboardMetricFormat
{
    Number,
    Currency
}

public sealed class AdminDashboardKpiViewModel
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string IconClass { get; init; } = "fa-circle-info";
    public string Tone { get; init; } = "info";
    public decimal Value { get; init; }
    public decimal PreviousValue { get; init; }
    public decimal? ChangePercent { get; init; }
    public bool LowerIsBetter { get; init; }
    public AdminDashboardMetricFormat Format { get; init; }
}

public sealed class AdminDashboardTrendPointViewModel
{
    public DateOnly Date { get; init; }
    public string Label { get; init; } = string.Empty;
    public decimal PaidRevenue { get; init; }
    public decimal RefundAmount { get; init; }
    public decimal NetRevenue { get; init; }
    public int OrderCount { get; init; }
    public bool ShowAxisLabel { get; init; }
}

public sealed class AdminDashboardBreakdownItemViewModel
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal Amount { get; init; }
    public decimal Percentage { get; init; }
    public string Tone { get; init; } = "neutral";
}

public sealed class AdminDashboardTopProductViewModel
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public int OrderCount { get; init; }
    public decimal Revenue { get; init; }
    public decimal PercentageOfTopRevenue { get; init; }
}

public sealed class AdminDashboardRecentOrderViewModel
{
    public int OrderId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public decimal GrandTotal { get; init; }
    public OrderStatus OrderStatus { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public FulfillmentStatus FulfillmentStatus { get; init; }
    public DateTime CreatedAt { get; init; }
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
