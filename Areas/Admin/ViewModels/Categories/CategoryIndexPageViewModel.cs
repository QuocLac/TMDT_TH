using System.ComponentModel.DataAnnotations;

namespace WebApplication2.Areas.Admin.ViewModels.Categories;

public sealed class CategoryIndexPageViewModel
{
    public IReadOnlyList<CategoryListItemViewModel> Categories { get; init; } = [];

    public IReadOnlyList<CategoryOptionViewModel> ParentOptions { get; init; } = [];
}

public sealed class CategoryListItemViewModel
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string? ParentName { get; init; }

    public int ProductCount { get; init; }
}

public sealed class CategoryOptionViewModel
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;
}

public sealed class CategoryInputModel
{
    public int Id { get; init; }

    [Required(ErrorMessage = "Vui lòng nhập tên danh mục.")]
    [StringLength(255, ErrorMessage = "Tên danh mục không được vượt quá 255 ký tự.")]
    public string Name { get; init; } = string.Empty;

    public int? ParentId { get; init; }

    [Required(ErrorMessage = "Vui lòng nhập đường dẫn SEO.")]
    [StringLength(255, ErrorMessage = "Đường dẫn SEO không được vượt quá 255 ký tự.")]
    [RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Slug chỉ gồm chữ thường không dấu, số và dấu gạch ngang.")]
    public string Slug { get; init; } = string.Empty;

    [StringLength(255, ErrorMessage = "Meta title không được vượt quá 255 ký tự.")]
    public string? MetaTitle { get; init; }

    [StringLength(500, ErrorMessage = "Meta description không được vượt quá 500 ký tự.")]
    public string? MetaDescription { get; init; }
}
