using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
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
    public async Task<IActionResult> Details(
        string slug,
        CancellationToken cancellationToken)
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
                        SelectionLabel = variant.Color != null && variant.Size != null
                            ? variant.Color + " · " + variant.Size
                            : variant.Color ?? variant.Size ?? "Lựa chọn tiêu chuẩn",
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

        var variantIds = product.Variants
            .Select(item => item.Id)
            .ToArray();

        var selectionRows = await _context
            .Set<ProductVariantOptionSelection>()
            .AsNoTracking()
            .Where(selection =>
                variantIds.Contains(selection.VariantId)
                && selection.OptionGroup.IsActive
                && selection.OptionValue.IsActive)
            .Select(selection => new
            {
                selection.VariantId,
                GroupOrder = selection.OptionGroup.DisplayOrder,
                GroupId = selection.OptionGroupId,
                ValueOrder = selection.OptionValue.DisplayOrder,
                selection.OptionValue.Label
            })
            .OrderBy(selection => selection.GroupOrder)
            .ThenBy(selection => selection.GroupId)
            .ThenBy(selection => selection.ValueOrder)
            .ToArrayAsync(cancellationToken);

        var selectionLabelByVariantId = selectionRows
            .GroupBy(row => row.VariantId)
            .ToDictionary(
                group => group.Key,
                group => string.Join(
                    " · ",
                    group.Select(item => item.Label)));

        var variants = product.Variants
            .Select(item => new ProductVariantDetailsViewModel
            {
                Id = item.Id,
                Sku = item.Sku,
                SelectionLabel = selectionLabelByVariantId.TryGetValue(
                    item.Id,
                    out var dynamicLabel)
                    && !string.IsNullOrWhiteSpace(dynamicLabel)
                        ? dynamicLabel
                        : item.SelectionLabel,
                ImageUrl = item.ImageUrl,
                OriginalPrice = item.OriginalPrice,
                EffectivePrice = item.EffectivePrice,
                IsOnSale = item.IsOnSale,
                StockQuantity = item.StockQuantity
            })
            .ToArray();

        var specificationRows = await _context.Set<ProductAttributeValue>()
            .AsNoTracking()
            .Where(value =>
                value.ProductId == product.Id
                && value.AttributeDefinition.IsActive
                && value.AttributeDefinition.IsCustomerVisible
                && value.AttributeDefinition.CategoryAssignments.Any(assignment =>
                    assignment.CategoryId == product.CategoryId))
            .Select(value => new
            {
                value.AttributeDefinition.Name,
                value.AttributeDefinition.DataType,
                value.AttributeDefinition.Unit,
                value.TextValue,
                value.NumberValue,
                value.BooleanValue,
                value.DateValue,
                OptionLabel = value.Option != null ? value.Option.Label : null,
                GroupName = value.AttributeDefinition.CategoryAssignments
                    .Where(assignment => assignment.CategoryId == product.CategoryId)
                    .Select(assignment => assignment.GroupName)
                    .FirstOrDefault(),
                DisplayOrder = value.AttributeDefinition.CategoryAssignments
                    .Where(assignment => assignment.CategoryId == product.CategoryId)
                    .Select(assignment => assignment.DisplayOrder)
                    .FirstOrDefault()
            })
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Name)
            .ToArrayAsync(cancellationToken);

        var specificationGroups = specificationRows
            .Select(row => new
            {
                GroupName = string.IsNullOrWhiteSpace(row.GroupName)
                    ? "Thông tin chung"
                    : row.GroupName,
                row.DisplayOrder,
                row.Name,
                Value = FormatSpecificationValue(
                    row.DataType,
                    row.Unit,
                    row.TextValue,
                    row.NumberValue,
                    row.BooleanValue,
                    row.DateValue,
                    row.OptionLabel)
            })
            .Where(row => !string.IsNullOrWhiteSpace(row.Value))
            .GroupBy(row => row.GroupName)
            .Select(group => new ProductSpecificationGroupViewModel
            {
                Name = group.Key,
                DisplayOrder = group.Min(item => item.DisplayOrder),
                Items = group
                    .OrderBy(item => item.DisplayOrder)
                    .ThenBy(item => item.Name)
                    .Select(item => new ProductSpecificationItemViewModel
                    {
                        Name = item.Name,
                        Value = item.Value!,
                        DisplayOrder = item.DisplayOrder
                    })
                    .ToArray()
            })
            .OrderBy(group => group.DisplayOrder)
            .ThenBy(group => group.Name)
            .ToArray();

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
            Images = product.Images.Count == 0
                ? [new ProductMediaViewModel
                {
                    Url = "/images/no-image.png",
                    IsMain = true
                }]
                : product.Images,
            Variants = variants,
            SpecificationGroups = specificationGroups
        };

        return View(product);
    }

    [HttpGet("/Product/Details/{id:int}")]
    public async Task<IActionResult> LegacyDetails(
        int id,
        CancellationToken cancellationToken)
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

    private static string? FormatSpecificationValue(
        ProductAttributeDataType dataType,
        string? unit,
        string? textValue,
        decimal? numberValue,
        bool? booleanValue,
        DateTime? dateValue,
        string? optionLabel)
    {
        var culture = CultureInfo.GetCultureInfo("vi-VN");

        return dataType switch
        {
            ProductAttributeDataType.ShortText
                or ProductAttributeDataType.LongText =>
                string.IsNullOrWhiteSpace(textValue)
                    ? null
                    : textValue.Trim(),
            ProductAttributeDataType.Number when numberValue.HasValue =>
                string.IsNullOrWhiteSpace(unit)
                    ? numberValue.Value.ToString("0.####", culture)
                    : $"{numberValue.Value.ToString("0.####", culture)} {unit}",
            ProductAttributeDataType.Boolean when booleanValue.HasValue =>
                booleanValue.Value ? "Có" : "Không",
            ProductAttributeDataType.Date when dateValue.HasValue =>
                dateValue.Value.ToString("dd/MM/yyyy", culture),
            ProductAttributeDataType.SingleChoice =>
                string.IsNullOrWhiteSpace(optionLabel)
                    ? null
                    : optionLabel,
            _ => null
        };
    }
}
