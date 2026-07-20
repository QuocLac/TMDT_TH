using System.ComponentModel.DataAnnotations;

namespace WebApplication2.Areas.Admin.ViewModels.ProductOptions;

public sealed class ProductOptionIndexPageViewModel
{
    public string Query { get; init; } = string.Empty;

    public int Page { get; init; }

    public int TotalPages { get; init; }

    public int TotalItems { get; init; }

    public IReadOnlyList<ProductOptionIndexItemViewModel> Items { get; init; } = [];
}

public sealed class ProductOptionIndexItemViewModel
{
    public int ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public string CategoryName { get; init; } = string.Empty;

    public string? BrandName { get; init; }

    public string ImageUrl { get; init; } = "/images/no-image.png";

    public bool IsActive { get; init; }

    public int OptionGroupCount { get; init; }

    public int OptionValueCount { get; init; }

    public int SellableItemCount { get; init; }

    public int ConfiguredItemCount { get; init; }
}

public sealed class ProductOptionConfigureViewModel
{
    public int ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public string CategoryName { get; init; } = string.Empty;

    public string? BrandName { get; init; }

    public bool ProductIsActive { get; init; }

    public IReadOnlyList<ProductOptionGroupItemViewModel> Groups { get; init; } = [];

    public IReadOnlyList<ProductOptionVariantItemViewModel> Items { get; init; } = [];

    public bool HasLegacySelectionData { get; init; }

    public ProductOptionCombinationPreviewViewModel CombinationPreview { get; init; }
        = new();
}

public sealed class ProductOptionGroupItemViewModel
{
    public int Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int DisplayOrder { get; init; }

    public bool IsRequired { get; init; }

    public bool IsActive { get; init; }

    public IReadOnlyList<ProductOptionValueItemViewModel> Values { get; init; } = [];
}

public sealed class ProductOptionValueItemViewModel
{
    public int Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public int DisplayOrder { get; init; }

    public bool IsActive { get; init; }
}

public sealed class ProductOptionVariantItemViewModel
{
    public int VariantId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string LegacyLabel { get; init; } = "Lựa chọn tiêu chuẩn";

    public decimal CurrentPrice { get; init; }

    public int StockQuantity { get; init; }

    public bool IsActive { get; init; }

    public bool IsComplete { get; init; }

    public string SelectionLabel { get; init; } = "Chưa cấu hình";

    public IReadOnlyDictionary<int, int> SelectionByGroupId { get; init; }
        = new Dictionary<int, int>();
}

public sealed class ProductOptionGroupInput
{
    public int Id { get; set; }

    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [StringLength(80)]
    [RegularExpression(
        "^[a-z][a-z0-9_]*$",
        ErrorMessage = "Mã nhóm chỉ gồm chữ thường, số và dấu gạch dưới.")]
    public string? Code { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên nhóm lựa chọn.")]
    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 9_999)]
    public int DisplayOrder { get; set; }

    public bool IsRequired { get; set; } = true;

    public bool IsActive { get; set; } = true;

    [Required(ErrorMessage = "Vui lòng nhập ít nhất một giá trị.")]
    public string ValuesText { get; set; } = string.Empty;
}

public sealed class ProductVariantSelectionInput
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int VariantId { get; set; }

    public Dictionary<int, int?> Selections { get; set; } = [];
}

public sealed class ProductOptionCombinationPreviewViewModel
{
    public int ActiveGroupCount { get; init; }

    public long TotalCombinationCount { get; init; }

    public int ExistingCombinationCount { get; init; }

    public long MissingCombinationCount { get; init; }

    public decimal SuggestedListPrice { get; init; }

    public IReadOnlyList<string> RequiredGroupsWithoutValues { get; init; } = [];

    public bool HasExistingConflict { get; init; }

    public bool IsOverLimit { get; init; }

    public int MaximumCombinationCount { get; init; }

    public bool CanGenerate =>
        ActiveGroupCount > 0
        && RequiredGroupsWithoutValues.Count == 0
        && !HasExistingConflict
        && !IsOverLimit
        && MissingCombinationCount > 0;
}

public sealed class ProductOptionCombinationGenerateInput
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [Range(
        typeof(decimal),
        "0.01",
        "9999999999999999",
        ErrorMessage = "Giá niêm yết phải lớn hơn 0.",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true)]
    public decimal ListPrice { get; set; }

    [Range(
        0,
        int.MaxValue,
        ErrorMessage = "Số lượng có thể bán không được âm.")]
    public int StockQuantity { get; set; }

    public bool ActivateNewItems { get; set; } = true;
}
