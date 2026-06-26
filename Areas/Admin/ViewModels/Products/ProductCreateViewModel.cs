using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using WebApplication2.Areas.Admin.ViewModels.Validation;

namespace WebApplication2.Areas.Admin.ViewModels.Products;

public sealed class ProductCreateViewModel
{
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

    public bool IsActive { get; set; } = true;

    [StringLength(255)]
    public string? MetaTitle { get; set; }

    [StringLength(500)]
    public string? MetaDescription { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ảnh đại diện.")]
    public IFormFile? MainImage { get; set; }

    public List<IFormFile> GalleryImages { get; set; } = [];

    [MinLength(1, ErrorMessage = "Sản phẩm phải có ít nhất một biến thể.")]
    public List<ProductVariantCreateInput> Variants { get; set; } = [new()];

    public List<int> SelectedPromotionIds { get; set; } = [];

    public IReadOnlyList<SelectListItem> CategoryOptions { get; set; } = [];

    public IReadOnlyList<SelectListItem> BrandOptions { get; set; } = [];

    public IReadOnlyList<SelectListItem> PromotionOptions { get; set; } = [];
}

public sealed class ProductVariantCreateInput
{

    [StringLength(50)]
    public string? Color { get; set; }

    [StringLength(50)]
    public string? Size { get; set; }

    [MoneyRange(ErrorMessage = "Giá niêm yết phải lớn hơn 0.")]
    public decimal Price { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Tồn kho không được âm.")]
    public int StockQuantity { get; set; }

    public bool IsActive { get; set; } = true;

    public IFormFile? Image { get; set; }
}
