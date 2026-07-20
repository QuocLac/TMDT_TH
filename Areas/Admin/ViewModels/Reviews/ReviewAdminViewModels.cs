using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;

namespace WebApplication2.Areas.Admin.ViewModels.Reviews;

public sealed class ReviewAdminIndexViewModel
{
    public string Search { get; init; } = string.Empty;

    public ProductReviewStatus? Status { get; init; }

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

    public ProductReviewStatus Status { get; init; }

    public DateTime SubmittedAt { get; init; }

    public bool HasReply { get; init; }
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

    public ProductReviewStatus Status { get; init; }

    public DateTime SubmittedAt { get; init; }

    public DateTime? EditedAt { get; init; }

    public string? ModeratedBy { get; init; }

    public DateTime? ModeratedAt { get; init; }

    public string? ModerationNote { get; init; }

    public string RowVersion { get; init; } = string.Empty;

    public IReadOnlyList<ReviewAdminMediaViewModel> Media { get; init; } = [];

    public string? ReplyContent { get; init; }

    public string? RepliedBy { get; init; }

    public DateTime? RepliedAt { get; init; }
}

public sealed class ReviewAdminMediaViewModel
{
    public ProductReviewMediaType Type { get; init; }

    public string Url { get; init; } = string.Empty;
}

public sealed class ModerateReviewInput
{
    [Required]
    public string Action { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ReplyReviewInput
{
    [Required]
    [StringLength(1000, MinimumLength = 10)]
    public string Content { get; set; } = string.Empty;
}
