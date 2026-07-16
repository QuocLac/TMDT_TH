using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.Home;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
public sealed class HomeController : Controller
{
    private const int LowStockThreshold = 5;
    private const int DashboardItemLimit = 7;

    private static readonly ReturnRequestStatus[] OpenReturnStatuses =
    [
        ReturnRequestStatus.Requested,
        ReturnRequestStatus.UnderReview,
        ReturnRequestStatus.Approved,
        ReturnRequestStatus.AwaitingReturnShipment,
        ReturnRequestStatus.AwaitingPickup,
        ReturnRequestStatus.ReturnInTransit,
        ReturnRequestStatus.ReceivedAtWarehouse,
        ReturnRequestStatus.Inspecting,
        ReturnRequestStatus.RefundPending
    ];

    private readonly ApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public HomeController(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var todayUtc = nowUtc.Date;
        var tomorrowUtc = todayUtc.AddDays(1);

        var activeVariants = _dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.IsActive && variant.Product.IsActive);

        var availableCampaigns = _dbContext.PriceCampaigns
            .AsNoTracking()
            .Where(campaign =>
                campaign.IsActive
                && campaign.EndDate != null
                && campaign.EndDate > nowUtc);

        var model = new AdminDashboardViewModel
        {
            GeneratedAtUtc = nowUtc,

            OrdersTodayCount = await _dbContext.Orders
                .AsNoTracking()
                .CountAsync(
                    order =>
                        order.CreatedAt >= todayUtc
                        && order.CreatedAt < tomorrowUtc,
                    cancellationToken),

            PaidRevenueToday = await _dbContext.PaymentTransactions
                .AsNoTracking()
                .Where(transaction =>
                    transaction.Status == PaymentStatus.Paid
                    && transaction.CompletedAt != null
                    && transaction.CompletedAt >= todayUtc
                    && transaction.CompletedAt < tomorrowUtc)
                .SumAsync(
                    transaction => (decimal?)transaction.Amount,
                    cancellationToken)
                ?? 0m,

            AwaitingConfirmationCount = await _dbContext.Orders
                .AsNoTracking()
                .CountAsync(
                    order => order.OrderStatus == OrderStatus.Placed,
                    cancellationToken),

            ReadyToShipCount = await _dbContext.Orders
                .AsNoTracking()
                .CountAsync(
                    order => order.FulfillmentStatus == FulfillmentStatus.ReadyToShip,
                    cancellationToken),

            ShippingExceptionCount = await _dbContext.Shipments
                .AsNoTracking()
                .CountAsync(
                    shipment =>
                        shipment.Direction == ShipmentDirection.Outbound
                        && (shipment.Status == ShipmentStatus.DeliveryFailed
                            || shipment.Status == ShipmentStatus.Exception),
                    cancellationToken),

            OpenReturnCount = await _dbContext.ReturnRequests
                .AsNoTracking()
                .CountAsync(
                    request => OpenReturnStatuses.Contains(request.Status),
                    cancellationToken),

            ActiveProductCount = await _dbContext.Products
                .AsNoTracking()
                .CountAsync(product => product.IsActive, cancellationToken),

            LowStockVariantCount = await activeVariants
                .CountAsync(
                    variant =>
                        variant.StockQuantity > 0
                        && variant.StockQuantity <= LowStockThreshold,
                    cancellationToken),

            OutOfStockVariantCount = await activeVariants
                .CountAsync(
                    variant => variant.StockQuantity == 0,
                    cancellationToken),

            RunningCampaignCount = await availableCampaigns
                .CountAsync(
                    campaign => campaign.StartDate <= nowUtc,
                    cancellationToken),

            RecentOrders = await _dbContext.Orders
                .AsNoTracking()
                .OrderByDescending(order => order.CreatedAt)
                .Take(DashboardItemLimit)
                .Select(order => new AdminDashboardRecentOrderViewModel
                {
                    OrderId = order.Id,
                    Code = order.Code,
                    CustomerName = order.CustomerName,
                    GrandTotal = order.GrandTotal,
                    OrderStatus = order.OrderStatus,
                    PaymentStatus = order.PaymentStatus,
                    FulfillmentStatus = order.FulfillmentStatus,
                    CreatedAt = order.CreatedAt
                })
                .ToListAsync(cancellationToken),

            LowStockItems = await activeVariants
                .Where(variant => variant.StockQuantity <= LowStockThreshold)
                .OrderBy(variant => variant.StockQuantity)
                .ThenBy(variant => variant.Product.Name)
                .ThenBy(variant => variant.SKU)
                .Take(DashboardItemLimit)
                .Select(variant => new AdminDashboardLowStockItemViewModel
                {
                    ProductId = variant.ProductId,
                    ProductName = variant.Product.Name,
                    VariantId = variant.Id,
                    SKU = variant.SKU,
                    StockQuantity = variant.StockQuantity,
                    CurrentPrice = variant.CurrentPrice
                })
                .ToListAsync(cancellationToken),

            Campaigns = await availableCampaigns
                .OrderBy(campaign => campaign.StartDate <= nowUtc ? 0 : 1)
                .ThenBy(campaign => campaign.StartDate)
                .Take(DashboardItemLimit)
                .Select(campaign => new AdminDashboardCampaignItemViewModel
                {
                    CampaignId = campaign.Id,
                    Name = campaign.Name,
                    StartDate = campaign.StartDate,
                    EndDate = campaign.EndDate!.Value,
                    IsRunning = campaign.StartDate <= nowUtc,
                    VariantCount = campaign.CampaignItems.Count
                })
                .ToListAsync(cancellationToken)
        };

        return View(model);
    }
}
