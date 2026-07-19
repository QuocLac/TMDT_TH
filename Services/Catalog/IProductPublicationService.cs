namespace WebApplication2.Services.Catalog;

public interface IProductPublicationService
{
    Task<ProductPublicationReview?> ReviewAsync(
        int productId,
        CancellationToken cancellationToken);

    Task<ProductPublicationChangeResult> SetVisibilityAsync(
        int productId,
        bool publish,
        CancellationToken cancellationToken);

    Task<ProductPublicationBatchResult> SetVisibilityBatchAsync(
        IReadOnlyCollection<int> productIds,
        bool publish,
        CancellationToken cancellationToken);

    Task<ProductPublicationReconciliationResult> ReconcilePublishedAsync(
        int maximumProducts,
        CancellationToken cancellationToken);
}

public sealed record ProductPublicationReview(
    int ProductId,
    string ProductName,
    bool IsPublished,
    bool CanPublish,
    IReadOnlyList<string> Issues);

public sealed record ProductPublicationChangeResult(
    bool Success,
    int ProductId,
    string ProductName,
    bool IsPublished,
    IReadOnlyList<string> Issues);

public sealed record ProductPublicationBatchItemResult(
    int ProductId,
    string ProductName,
    bool Success,
    IReadOnlyList<string> Issues);

public sealed record ProductPublicationBatchResult(
    int RequestedCount,
    int ChangedCount,
    int FailedCount,
    IReadOnlyList<ProductPublicationBatchItemResult> Items);


public sealed record ProductPublicationReconciliationItem(
    int ProductId,
    string ProductName,
    IReadOnlyList<string> Issues);

public sealed record ProductPublicationReconciliationResult(
    int ScannedCount,
    int HiddenCount,
    bool ReachedLimit,
    IReadOnlyList<ProductPublicationReconciliationItem> HiddenItems);
