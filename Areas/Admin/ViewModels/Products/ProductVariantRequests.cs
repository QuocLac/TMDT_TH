using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using WebApplication2.Areas.Admin.ViewModels.Validation;

namespace WebApplication2.Areas.Admin.ViewModels.Products;

public sealed class UpdateVariantPriceRequest
{
    [Range(1, int.MaxValue)]
    public int VariantId { get; set; }

    [MoneyRange(ErrorMessage = "Giá mới phải lớn hơn 0.")]
    public decimal NewPrice { get; set; }

    [StringLength(255)]
    public string? Note { get; set; }

    [Required(ErrorMessage = "Thiếu phiên bản dữ liệu. Vui lòng tải lại trang.")]
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveProductVariantRequest
{
    public int VariantId { get; set; }

    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [StringLength(50)]
    public string? Color { get; set; }

    [StringLength(50)]
    public string? Size { get; set; }

    [MoneyRange(ErrorMessage = "Giá niêm yết phải lớn hơn 0.")]
    public decimal Price { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Tồn kho không được âm.")]
    public int StockQuantity { get; set; }

    public bool IsActive { get; set; } = true;

    public IFormFile? VariantImage { get; set; }

    public string? RowVersion { get; set; }
}

public sealed class ToggleVariantStatusRequest
{
    [Range(1, int.MaxValue)]
    public int VariantId { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}
