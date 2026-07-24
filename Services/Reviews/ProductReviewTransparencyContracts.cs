using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Reviews;

public sealed record ProductReviewConversationMessageSnapshot(
    long MessageId,
    ProductReviewMessageAuthorType AuthorType,
    string AuthorName,
    string Content,
    DateTime SentAt,
    bool IsLegacyReply = false);

public sealed record ProductReviewExperienceItemSnapshot(
    long ReviewId,
    string ReviewerName,
    byte Rating,
    string? Title,
    string Content,
    string? SelectionLabel,
    bool IsVerifiedPurchase,
    DateTime SubmittedAt,
    DateTime? EditedAt,
    IReadOnlyList<ProductReviewMediaSnapshot> Media,
    IReadOnlyList<ProductReviewConversationMessageSnapshot> Conversation,
    bool CanCustomerReply);

public sealed record ProductReviewExperiencePageSnapshot(
    int ProductId,
    string ProductName,
    string ProductSlug,
    int Page,
    int PageSize,
    int TotalPages,
    int TotalCount,
    int FilteredCount,
    int MediaReviewCount,
    double AverageRating,
    IReadOnlyDictionary<int, int> RatingCounts,
    int? RatingFilter,
    bool MediaOnly,
    IReadOnlyList<ProductReviewExperienceItemSnapshot> Items)
{
    public bool HasMore => Page < TotalPages;
}

public sealed class ProductReviewExperienceComponentViewModel
{
    public ProductReviewExperiencePageSnapshot Feed { get; init; } = null!;
}

public sealed class ProductReviewFeedViewModel
{
    public ProductReviewExperiencePageSnapshot Feed { get; init; } = null!;
}

public sealed class ProductReviewCustomerMessageInput
{
    [Required(ErrorMessage = "Vui lòng nhập nội dung phản hồi.")]
    [StringLength(
        ProductReviewTransparencyPolicy.MaximumMessageLength,
        MinimumLength = ProductReviewTransparencyPolicy.MinimumMessageLength,
        ErrorMessage = "Phản hồi cần từ 5 đến 1000 ký tự.")]
    public string Content { get; set; } = string.Empty;
}

public static class ProductReviewTransparencyPolicy
{
    public const int InitialPageSize = 4;
    public const int MinimumMessageLength = 5;
    public const int MaximumMessageLength = 1000;
    public const int MaximumConversationMessages = 40;
}
