using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Reviews;

namespace WebApplication2.Areas.Admin.ViewModels.Reviews;

public sealed class ReviewAdminIndexViewModel
{
    public string Search { get; init; } = string.Empty;

    public byte? Rating { get; init; }

    public IReadOnlyList<ReviewAdminListItemViewModel> Items { get; init; } = [];
}

public sealed class ReviewAdminListItemViewModel
{
    public long Id { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public string OrderCode { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public byte Rating { get; init; }

    public DateTime SubmittedAt { get; init; }

    public int ConversationCount { get; init; }
}

public sealed class ReviewAdminDetailsViewModel
{
    public long Id { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public string ProductSlug { get; init; } = string.Empty;

    public string OrderCode { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public string CustomerEmail { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public string? SelectionLabel { get; init; }

    public byte Rating { get; init; }

    public string? Title { get; init; }

    public string Content { get; init; } = string.Empty;

    public DateTime SubmittedAt { get; init; }

    public DateTime? EditedAt { get; init; }

    public IReadOnlyList<ReviewAdminMediaViewModel> Media { get; init; } = [];

    public IReadOnlyList<ProductReviewConversationMessageSnapshot>
        Conversation { get; init; } = [];
}

public sealed class ReviewAdminMediaViewModel
{
    public ProductReviewMediaType Type { get; init; }

    public string Url { get; init; } = string.Empty;
}

public sealed class ReplyReviewInput
{
    [Required]
    [StringLength(
        ProductReviewTransparencyPolicy.MaximumMessageLength,
        MinimumLength = ProductReviewTransparencyPolicy.MinimumMessageLength)]
    public string Content { get; set; } = string.Empty;
}
