using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Reviews;

namespace WebApplication2.ViewModels.Storefront.Reviews;

public sealed class ReviewableItemsPageViewModel
{
    public IReadOnlyList<CustomerReviewableItemSnapshot> Items { get; init; } = [];
}

public sealed class ProductReviewEditorPageViewModel
{
    public ProductReviewEditorSnapshot Item { get; init; } = null!;

    public ProductReviewInputModel Form { get; init; } = new();
}

public sealed class ProductReviewInputModel
{
    [Range(1, 5, ErrorMessage = "Vui lòng chọn từ 1 đến 5 sao.")]
    public byte Rating { get; set; } = 5;

    [StringLength(120)]
    public string? Title { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập nội dung đánh giá.")]
    [StringLength(2000, MinimumLength = 20)]
    public string Content { get; set; } = string.Empty;

    public string? MediaUrls { get; set; }

    public string? RowVersion { get; set; }
}

public sealed class MyProductReviewsPageViewModel
{
    public IReadOnlyList<CustomerProductReviewSnapshot> Items { get; init; } = [];
}

public sealed class ProductReviewComponentViewModel
{
    public ProductReviewSummarySnapshot Summary { get; init; } = null!;
}

public sealed class ProductReviewPublicPageViewModel
{
    public ProductReviewPageSnapshot Page { get; init; } = null!;
}
