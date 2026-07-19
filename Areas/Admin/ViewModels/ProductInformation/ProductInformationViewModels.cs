using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using WebApplication2.Models.Enums;

namespace WebApplication2.Areas.Admin.ViewModels.ProductInformation;

public sealed class ProductAttributeDefinitionIndexViewModel
{
    public IReadOnlyList<ProductAttributeDefinitionItemViewModel> Items { get; init; } = [];

    public ProductAttributeDefinitionInput Input { get; init; } = new();
}

public sealed class ProductAttributeDefinitionItemViewModel
{
    public int Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? HelpText { get; init; }

    public ProductAttributeDataType DataType { get; init; }

    public string? Unit { get; init; }

    public bool IsFilterable { get; init; }

    public bool IsComparable { get; init; }

    public bool IsCustomerVisible { get; init; }

    public bool IsActive { get; init; }

    public int CategoryCount { get; init; }

    public int ProductValueCount { get; init; }

    public IReadOnlyList<string> Options { get; init; } = [];
}

public sealed class ProductAttributeDefinitionInput
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã trường dữ liệu.")]
    [StringLength(80)]
    [RegularExpression(
        "^[a-z][a-z0-9_]*$",
        ErrorMessage = "Mã trường chỉ gồm chữ thường, số và dấu gạch dưới.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên hiển thị.")]
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? HelpText { get; set; }

    public ProductAttributeDataType DataType { get; set; } =
        ProductAttributeDataType.ShortText;

    [StringLength(30)]
    public string? Unit { get; set; }

    public bool IsFilterable { get; set; }

    public bool IsComparable { get; set; }

    public bool IsCustomerVisible { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public string? ChoiceLabels { get; set; }
}

public sealed class CategoryProductInformationViewModel
{
    public int CategoryId { get; set; }

    public string CategoryName { get; init; } = string.Empty;

    public IReadOnlyList<SelectListItem> CategoryOptions { get; init; } = [];

    public List<CategoryAttributeAssignmentInput> Attributes { get; set; } = [];
}

public sealed class CategoryAttributeAssignmentInput
{
    public int AttributeDefinitionId { get; set; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string DataTypeText { get; init; } = string.Empty;

    public bool Selected { get; set; }

    [StringLength(100)]
    public string? GroupName { get; set; }

    public bool IsRequired { get; set; }

    [Range(0, 9_999)]
    public int DisplayOrder { get; set; }
}

public sealed class ProductInformationEditViewModel
{
    public int ProductId { get; set; }

    public string ProductName { get; init; } = string.Empty;

    public int CategoryId { get; init; }

    public string CategoryName { get; init; } = string.Empty;

    public List<ProductInformationFieldInput> Fields { get; set; } = [];
}

public sealed class ProductInformationFieldInput
{
    public int AttributeDefinitionId { get; set; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? HelpText { get; init; }

    public ProductAttributeDataType DataType { get; init; }

    public string? Unit { get; init; }

    public string GroupName { get; init; } = "Thông tin chung";

    public bool IsRequired { get; init; }

    public int DisplayOrder { get; init; }

    [StringLength(2_000)]
    public string? TextValue { get; set; }

    public decimal? NumberValue { get; set; }

    public bool? BooleanValue { get; set; }

    [System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Date)]
    public DateTime? DateValue { get; set; }

    public int? OptionId { get; set; }

    public IReadOnlyList<SelectListItem> OptionItems { get; init; } = [];
}

public static class ProductInformationDisplay
{
    public static string DataTypeText(ProductAttributeDataType dataType) => dataType switch
    {
        ProductAttributeDataType.ShortText => "Nội dung ngắn",
        ProductAttributeDataType.LongText => "Nội dung dài",
        ProductAttributeDataType.Number => "Số đo",
        ProductAttributeDataType.Boolean => "Có hoặc không",
        ProductAttributeDataType.Date => "Ngày",
        ProductAttributeDataType.SingleChoice => "Chọn một giá trị",
        _ => dataType.ToString()
    };
}
