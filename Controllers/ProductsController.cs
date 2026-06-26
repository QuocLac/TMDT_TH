using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.ViewModels.Storefront.Products;

namespace WebApplication2.Controllers;

[Route("products")]
public sealed class ProductsController : Controller
{
    private readonly ApplicationDbContext _context;

    public ProductsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return NotFound();
        }

        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var product = await _context.Products
            .AsNoTracking()
            .Where(item => item.IsActive && item.Slug == normalizedSlug)
            .Select(item => new ProductDetailsViewModel
            {
                Id = item.Id,
                Name = item.Name,
                Slug = item.Slug,
                Description = item.Description,
                MetaTitle = item.MetaTitle,
                MetaDescription = item.MetaDescription,
                CategoryId = item.CategoryId,
                CategoryName = item.Category.Name,
                CategorySlug = item.Category.Slug,
                BrandName = item.Brand != null ? item.Brand.Name : null,
                Images = item.Images
                    .OrderByDescending(image => image.IsMain)
                    .ThenBy(image => image.Id)
                    .Select(image => new ProductMediaViewModel
                    {
                        Url = image.ImageUrl,
                        IsMain = image.IsMain
                    })
                    .ToList(),
                Variants = item.Variants
                    .Where(variant =>
                        variant.IsActive
                        && variant.Price > 0
                        && variant.CurrentPrice > 0)
                    .OrderBy(variant => variant.Id)
                    .Select(variant => new ProductVariantDetailsViewModel
                    {
                        Id = variant.Id,
                        Sku = variant.SKU,
                        Color = variant.Color,
                        Size = variant.Size,
                        ImageUrl = variant.ImageUrl,
                        OriginalPrice = variant.Price,
                        EffectivePrice = variant.CurrentPrice,
                        IsOnSale = variant.CurrentPrice < variant.Price,
                        StockQuantity = variant.StockQuantity
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        if (product.Images.Count == 0)
        {
            product = new ProductDetailsViewModel
            {
                Id = product.Id,
                Name = product.Name,
                Slug = product.Slug,
                Description = product.Description,
                MetaTitle = product.MetaTitle,
                MetaDescription = product.MetaDescription,
                CategoryId = product.CategoryId,
                CategoryName = product.CategoryName,
                CategorySlug = product.CategorySlug,
                BrandName = product.BrandName,
                Images = [new ProductMediaViewModel { Url = "/images/no-image.png", IsMain = true }],
                Variants = product.Variants
            };
        }

        return View(product);
    }

    [HttpGet("/Product/Details/{id:int}")]
    public async Task<IActionResult> LegacyDetails(int id, CancellationToken cancellationToken)
    {
        var slug = await _context.Products
            .AsNoTracking()
            .Where(product => product.Id == id && product.IsActive)
            .Select(product => product.Slug)
            .SingleOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(slug)
            ? NotFound()
            : RedirectToActionPermanent(nameof(Details), new { slug });
    }
}
