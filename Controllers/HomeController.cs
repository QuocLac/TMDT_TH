using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.ViewModels.Shared;
using WebApplication2.ViewModels.Storefront.Home;

namespace WebApplication2.Controllers;

public sealed class HomeController : Controller
{
    private const int SuggestedProductLimit = 24;
    private const int FlashSaleLimit = 8;

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public HomeController(ApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? query,
        int? categoryId,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = query?.Trim() ?? string.Empty;
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var categories = await _context.Categories
            .AsNoTracking()
            .Where(category => category.ParentId == null)
            .OrderBy(category => category.Name)
            .Select(category => new CategoryCardViewModel
            {
                Id = category.Id,
                Name = category.Name,
                Slug = category.Slug
            })
            .Take(12)
            .ToListAsync(cancellationToken);

        var suggestedQuery = _context.Products
            .AsNoTracking()
            .Where(product => product.IsActive
                && product.Variants.Any(variant =>
                    variant.IsActive
                    && variant.Price > 0
                    && variant.CurrentPrice > 0));

        if (!string.IsNullOrEmpty(normalizedQuery))
        {
            suggestedQuery = suggestedQuery.Where(product =>
                product.Name.Contains(normalizedQuery)
                || product.Variants.Any(variant => variant.SKU.Contains(normalizedQuery)));
        }

        if (categoryId is > 0)
        {
            suggestedQuery = suggestedQuery.Where(product => product.CategoryId == categoryId.Value);
        }

        var suggestedProducts = await LoadProductCardsAsync(
            suggestedQuery.OrderByDescending(product => product.CreatedAt),
            SuggestedProductLimit,
            nowUtc,
            cancellationToken);

        IReadOnlyList<ProductCardViewModel> flashSaleProducts = [];
        if (string.IsNullOrEmpty(normalizedQuery) && categoryId is null)
        {
            var flashSaleQuery = _context.Products
                .AsNoTracking()
                .Where(product =>
                    product.IsActive
                    && product.Variants.Any(variant =>
                        variant.IsActive
                        && variant.CurrentPrice > 0
                        && variant.CurrentPrice < variant.Price
                        && variant.CampaignItems.Any(item =>
                            item.Campaign.IsActive
                            && item.Campaign.StartDate <= nowUtc
                            && item.Campaign.EndDate > nowUtc
                            && item.NewPrice == variant.CurrentPrice)))
                .OrderBy(product => product.Id);

            flashSaleProducts = await LoadProductCardsAsync(
                flashSaleQuery,
                FlashSaleLimit,
                nowUtc,
                cancellationToken);
        }

        var model = new HomePageViewModel
        {
            Query = normalizedQuery,
            CategoryId = categoryId,
            Categories = categories,
            FlashSaleProducts = flashSaleProducts,
            SuggestedProducts = suggestedProducts,
            FlashSaleEndsAt = flashSaleProducts
                .Where(product => product.SaleEndsAt.HasValue)
                .Select(product => product.SaleEndsAt)
                .Min()
        };

        return View(model);
    }

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(int? statusCode = null)
    {
        var effectiveStatusCode = statusCode ?? StatusCodes.Status500InternalServerError;
        Response.StatusCode = effectiveStatusCode;

        return View(new ErrorPageViewModel
        {
            StatusCode = effectiveStatusCode,
            Title = effectiveStatusCode == StatusCodes.Status404NotFound
                ? "Không tìm thấy trang"
                : "Đã xảy ra lỗi",
            Message = effectiveStatusCode == StatusCodes.Status404NotFound
                ? "Nội dung bạn cần không tồn tại hoặc đã được chuyển sang địa chỉ khác."
                : "Hệ thống chưa thể xử lý yêu cầu. Vui lòng thử lại sau.",
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }

    private static async Task<IReadOnlyList<ProductCardViewModel>> LoadProductCardsAsync(
        IQueryable<Product> query,
        int limit,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var products = await query
            .Take(limit)
            .Select(product => new
            {
                product.Id,
                product.Slug,
                product.Name,
                ImageUrl = product.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.Id)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault(),
                Variants = product.Variants
                    .Where(variant =>
                        variant.IsActive
                        && variant.Price > 0
                        && variant.CurrentPrice > 0)
                    .Select(variant => new
                    {
                        variant.Price,
                        variant.CurrentPrice,
                        variant.StockQuantity,
                        SaleEndsAt = variant.CampaignItems
                            .Where(item =>
                                item.Campaign.IsActive
                                && item.Campaign.StartDate <= nowUtc
                                && item.Campaign.EndDate > nowUtc
                                && item.NewPrice == variant.CurrentPrice)
                            .OrderByDescending(item => item.Campaign.StartDate)
                            .ThenByDescending(item => item.CampaignId)
                            .Select(item => (DateTime?)item.Campaign.EndDate)
                            .FirstOrDefault()
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return products
            .Select(product =>
            {
                var selectedVariant = product.Variants
                    .OrderBy(variant => variant.CurrentPrice)
                    .ThenBy(variant => variant.Price)
                    .FirstOrDefault();

                if (selectedVariant is null)
                {
                    return null;
                }

                var isOnSale = selectedVariant.CurrentPrice < selectedVariant.Price;
                var discountPercentage = isOnSale
                    ? (int)Math.Round(
                        (1 - selectedVariant.CurrentPrice / selectedVariant.Price) * 100,
                        MidpointRounding.AwayFromZero)
                    : 0;

                return new ProductCardViewModel
                {
                    Id = product.Id,
                    Slug = product.Slug,
                    Name = product.Name,
                    ImageUrl = string.IsNullOrWhiteSpace(product.ImageUrl)
                        ? "/images/no-image.png"
                        : product.ImageUrl,
                    OriginalPrice = selectedVariant.Price,
                    EffectivePrice = selectedVariant.CurrentPrice,
                    DiscountPercentage = discountPercentage,
                    IsOnSale = isOnSale,
                    StockQuantity = selectedVariant.StockQuantity,
                    SaleEndsAt = selectedVariant.SaleEndsAt.HasValue
                        ? new DateTimeOffset(
                            DateTime.SpecifyKind(selectedVariant.SaleEndsAt.Value, DateTimeKind.Utc))
                        : null
                };
            })
            .Where(product => product is not null)
            .Cast<ProductCardViewModel>()
            .ToList();
    }
}
