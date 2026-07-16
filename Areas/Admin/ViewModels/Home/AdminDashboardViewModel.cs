using WebApplication2.Models.Enums;

namespace WebApplication2.Areas.Admin.ViewModels.Home;

public sealed class AdminDashboardViewModel
{
    public DateTime GeneratedAtUtc { get; init; }

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
