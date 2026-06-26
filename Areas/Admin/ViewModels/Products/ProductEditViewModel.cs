using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace WebApplication2.Areas.Admin.ViewModels.Products;

public sealed class ProductEditViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm.")]
    [StringLength(255)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập đường dẫn SEO.")]
    [StringLength(255)]
    [RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Slug chỉ gồm chữ thường, số và dấu gạch ngang.")]
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn danh mục.")]
    public int CategoryId { get; set; }

    public int? BrandId { get; set; }

    public bool IsActive { get; set; }

    [StringLength(255)]
    public string? MetaTitle { get; set; }

    [StringLength(500)]
    public string? MetaDescription { get; set; }

    public string? CurrentMainImageUrl { get; set; }

    public IReadOnlyList<ProductImageItemViewModel> CurrentGalleryImages { get; set; } = [];

    public IFormFile? MainImage { get; set; }

    public List<IFormFile> GalleryImages { get; set; } = [];

    public List<int> SelectedPromotionIds { get; set; } = [];

    public IReadOnlyList<ProductVariantItemViewModel> Variants { get; set; } = [];

    public IReadOnlyList<SelectListItem> CategoryOptions { get; set; } = [];

    public IReadOnlyList<SelectListItem> BrandOptions { get; set; } = [];

    public IReadOnlyList<SelectListItem> PromotionOptions { get; set; } = [];
}

public sealed class ProductImageItemViewModel
{
    public int Id { get; init; }

    public string Url { get; init; } = string.Empty;
}

public sealed class ProductVariantItemViewModel
{
    public int Id { get; init; }

    public string SKU { get; init; } = string.Empty;

    public string? Color { get; init; }

    public string? Size { get; init; }

    public decimal Price { get; init; }

    public decimal CurrentPrice { get; init; }

    public int StockQuantity { get; init; }

    public bool IsActive { get; init; }

    public string? ImageUrl { get; init; }

    public string RowVersion { get; init; } = string.Empty;
}

public sealed class ProductIndexPageViewModel
{
    public string Query { get; init; } = string.Empty;

    public int Page { get; init; }

    public int TotalPages { get; init; }

    public int TotalItems { get; init; }

    public IReadOnlyList<ProductIndexItemViewModel> Items { get; init; } = [];
}

public sealed class ProductIndexItemViewModel
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string CategoryName { get; init; } = string.Empty;

    public string? BrandName { get; init; }

    public string ImageUrl { get; init; } = "/images/no-image.png";

    public bool IsActive { get; init; }

    public DateTime CreatedAt { get; init; }

    public IReadOnlyList<ProductVariantItemViewModel> Variants { get; init; } = [];
}
