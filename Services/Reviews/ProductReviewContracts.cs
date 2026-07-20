using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Reviews;

public sealed record ProductReviewMediaCommand(
    ProductReviewMediaType Type,
    string Url);

public sealed record SubmitProductReviewCommand(
    int OrderItemId,
    byte Rating,
    string? Title,
    string Content,
    string? RowVersion,
    IReadOnlyCollection<ProductReviewMediaCommand> Media);

public sealed record CustomerReviewableItemSnapshot(
    int OrderItemId,
    string OrderCode,
    int ProductId,
    string ProductName,
    string ProductSlug,
    string Sku,
    string? SelectionLabel,
    string ImageUrl,
    DateTime DeliveredAt,
    DateTime ReviewDeadlineAt,
    bool HasReview,
    ProductReviewStatus? ReviewStatus,
    bool CanCreate,
    bool CanEdit,
    string StateMessage);

public sealed record ProductReviewEditorSnapshot(
    int OrderItemId,
    string OrderCode,
    int ProductId,
    string ProductName,
    string ProductSlug,
    string Sku,
    string? SelectionLabel,
    string ImageUrl,
    DateTime DeliveredAt,
    DateTime ReviewDeadlineAt,
    long? ReviewId,
    byte Rating,
    string? Title,
    string Content,
    ProductReviewStatus? Status,
    string? RowVersion,
    bool CanSubmit,
    string Message,
    IReadOnlyList<ProductReviewMediaSnapshot> Media);

public sealed record ProductReviewMediaSnapshot(
    ProductReviewMediaType Type,
    string Url);

public sealed record CustomerProductReviewSnapshot(
    long ReviewId,
    int OrderItemId,
    int ProductId,
    string ProductName,
    string ProductSlug,
    string OrderCode,
    string Sku,
    string? SelectionLabel,
    string ImageUrl,
    byte Rating,
    string? Title,
    string Content,
    ProductReviewStatus Status,
    DateTime SubmittedAt,
    DateTime? EditedAt,
    string? ModerationNote,
    string? ReplyContent,
    DateTime? RepliedAt,
    bool CanEdit,
    IReadOnlyList<ProductReviewMediaSnapshot> Media);

public sealed record PublicProductReviewSnapshot(
    long ReviewId,
    string ReviewerName,
    byte Rating,
    string? Title,
    string Content,
    string? SelectionLabel,
    bool IsVerifiedPurchase,
    DateTime SubmittedAt,
    IReadOnlyList<ProductReviewMediaSnapshot> Media,
    string? ReplyContent,
    string? RepliedBy,
    DateTime? RepliedAt);

public sealed record ProductReviewSummarySnapshot(
    int ProductId,
    int TotalCount,
    double AverageRating,
    IReadOnlyDictionary<int, int> RatingCounts,
    IReadOnlyList<PublicProductReviewSnapshot> Items);

public sealed record ProductReviewPageSnapshot(
    int ProductId,
    string ProductName,
    string ProductSlug,
    int Page,
    int TotalPages,
    ProductReviewSummarySnapshot Summary,
    IReadOnlyList<PublicProductReviewSnapshot> Items);

public enum ReviewModerationAction
{
    Publish,
    Hide,
    Reject
}

public interface IProductReviewService
{
    Task<IReadOnlyList<CustomerReviewableItemSnapshot>> GetReviewableItemsAsync(
        int customerId,
        CancellationToken cancellationToken);

    Task<ProductReviewEditorSnapshot?> GetEditorAsync(
        int customerId,
        int orderItemId,
        CancellationToken cancellationToken);

    Task<long> SubmitAsync(
        int customerId,
        SubmitProductReviewCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerProductReviewSnapshot>> GetCustomerReviewsAsync(
        int customerId,
        CancellationToken cancellationToken);

    Task<ProductReviewSummarySnapshot> GetProductSummaryAsync(
        int productId,
        int take,
        CancellationToken cancellationToken);

    Task<ProductReviewPageSnapshot?> GetProductPageAsync(
        int productId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task ModerateAsync(
        long reviewId,
        ReviewModerationAction action,
        byte[] rowVersion,
        string actor,
        string note,
        CancellationToken cancellationToken);

    Task UpsertReplyAsync(
        long reviewId,
        string actor,
        string content,
        CancellationToken cancellationToken);
}

public sealed class ProductReviewRuleException : Exception
{
    public ProductReviewRuleException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
