using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.Home;
using WebApplication2.Models;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
public sealed class HomeController : Controller
{
    private const int LowStockThreshold = 5;
    private const int DashboardItemLimit = 6;

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

        var activeVariants = _dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.IsActive && variant.Product.IsActive);

        var availableCampaigns = _dbContext.PriceCampaigns
            .AsNoTracking()
            .Where(campaign =>
                campaign.IsActive &&
                campaign.EndDate > nowUtc);

        var model = new AdminDashboardViewModel
        {
            ActiveProductCount = await _dbContext.Products
                .AsNoTracking()
                .CountAsync(product => product.IsActive, cancellationToken),

            ActiveVariantCount = await activeVariants
                .CountAsync(cancellationToken),

            LowStockVariantCount = await activeVariants
                .CountAsync(
                    variant =>
                        variant.StockQuantity > 0 &&
                        variant.StockQuantity <= LowStockThreshold,
                    cancellationToken),

            OutOfStockVariantCount = await activeVariants
                .CountAsync(
                    variant => variant.StockQuantity == 0,
                    cancellationToken),

            RunningCampaignCount = await availableCampaigns
                .CountAsync(
                    campaign => campaign.StartDate <= nowUtc,
                    cancellationToken),

            UpcomingCampaignCount = await availableCampaigns
                .CountAsync(
                    campaign => campaign.StartDate > nowUtc,
                    cancellationToken),

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
                    EndDate = (DateTime)campaign.EndDate,
                    IsRunning = campaign.StartDate <= nowUtc,
                    VariantCount = campaign.CampaignItems.Count
                })
                .ToListAsync(cancellationToken)
        };

        return View(model);
    }
}
